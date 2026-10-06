using System.Globalization;
using System.Text;
using System.Text.Json;
using CesiumAI.Api.Configuration;
using CesiumAI.Api.Models;
using CesiumAI.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CesiumAI.Api.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController(
    IChatService chatService,
    IOptions<ChatEndpointOptions> endpointOptions,
    IOptions<JsonOptions> jsonOptions,
    IHostEnvironment hostEnvironment,
    ILogger<ChatController> logger) : ControllerBase
{
    private readonly IChatService _chatService =
        chatService ?? throw new ArgumentNullException(nameof(chatService));
    private readonly TimeSpan _agentTimeout =
        (endpointOptions ?? throw new ArgumentNullException(nameof(endpointOptions))).Value.Timeout;
    private readonly JsonSerializerOptions _jsonSerializerOptions =
        (jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions))).Value.JsonSerializerOptions;
    private readonly IHostEnvironment _hostEnvironment =
        hostEnvironment ?? throw new ArgumentNullException(nameof(hostEnvironment));
    private readonly ILogger<ChatController> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// 非流式：一轮结束后整包返回。保留给脚本、.http 调试与不支持流式读取的客户端。
    /// </summary>
    [HttpPost]
    [ProducesResponseType<ChatResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status499ClientClosedRequest)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<ChatResponse>> Post(ChatRequest request)
    {
        CancellationToken requestAborted = HttpContext.RequestAborted;
        using var serverTimeout = new CancellationTokenSource();
        serverTimeout.CancelAfter(_agentTimeout);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            requestAborted,
            serverTimeout.Token);
        CancellationToken operationToken = operationCancellation.Token;

        try
        {
            ChatResponse response = await _chatService.ChatAsync(request, operationToken);
            return Ok(response);
        }
        catch (OperationCanceledException exception) when (
            exception.CancellationToken == operationToken
            && requestAborted.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (OperationCanceledException exception) when (
            exception.CancellationToken == operationToken
            && serverTimeout.IsCancellationRequested
            && !requestAborted.IsCancellationRequested)
        {
            return StatusCode(
                StatusCodes.Status504GatewayTimeout,
                new
                {
                    error = "agent_timeout",
                    detail = TimeoutDetail()
                });
        }
    }

    /// <summary>
    /// 流式（SSE）：请求体与 <see cref="Post"/> 相同。请求体校验失败仍返回 400；
    /// 一旦通过校验即以 200 + <c>text/event-stream</c> 开始推送，此后的超时与失败以 <c>error</c> 事件告知。
    /// </summary>
    [HttpPost("stream")]
    [ProducesResponseType(typeof(void), StatusCodes.Status200OK, "text/event-stream")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task Stream(ChatRequest request)
    {
        CancellationToken requestAborted = HttpContext.RequestAborted;
        using var serverTimeout = new CancellationTokenSource();
        serverTimeout.CancelAfter(_agentTimeout);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            requestAborted,
            serverTimeout.Token);
        CancellationToken operationToken = operationCancellation.Token;

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        // 提示 nginx 等反向代理不要缓冲事件流。
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        try
        {
            await Response.StartAsync(requestAborted);

            await foreach (ChatStreamEvent streamEvent in _chatService
                .StreamAsync(request, operationToken)
                .WithCancellation(operationToken))
            {
                await WriteEventAsync(streamEvent, requestAborted);
            }
        }
        catch (OperationCanceledException) when (requestAborted.IsCancellationRequested)
        {
            // 客户端已断开：无处可写，直接结束。
        }
        catch (OperationCanceledException) when (serverTimeout.IsCancellationRequested)
        {
            // 下游（LLM HttpClient 等）抛出的取消异常可能携带其内部链接令牌，因此只按超时源判定。
            await WriteEventAsync(
                new ChatErrorStreamEvent("agent_timeout", TimeoutDetail()),
                requestAborted);
        }
        catch (Exception exception) when (!requestAborted.IsCancellationRequested)
        {
            _logger.LogError(exception, "Streaming chat turn failed.");
            string detail = _hostEnvironment.IsDevelopment()
                ? exception.Message
                : "Agent request failed.";
            await WriteEventAsync(new ChatErrorStreamEvent("agent_error", detail), requestAborted);
        }
    }

    private async Task WriteEventAsync(ChatStreamEvent streamEvent, CancellationToken cancellationToken)
    {
        // System.Text.Json 默认不输出原始换行，data 始终是单行 JSON，符合 SSE 单行 data 约定。
        string data = JsonSerializer.Serialize(streamEvent, streamEvent.GetType(), _jsonSerializerOptions);
        string frame = $"event: {streamEvent.EventName}\ndata: {data}\n\n";
        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(frame), cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private string TimeoutDetail() =>
        $"Agent request exceeded {_agentTimeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds.";
}
