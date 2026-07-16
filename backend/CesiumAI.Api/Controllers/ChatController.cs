using System.Globalization;
using CesiumAI.Api.Configuration;
using CesiumAI.Api.Models;
using CesiumAI.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CesiumAI.Api.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController(
    IChatService chatService,
    IOptions<ChatEndpointOptions> endpointOptions) : ControllerBase
{
    private readonly IChatService _chatService =
        chatService ?? throw new ArgumentNullException(nameof(chatService));
    private readonly TimeSpan _agentTimeout =
        (endpointOptions ?? throw new ArgumentNullException(nameof(endpointOptions))).Value.Timeout;

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
                    detail = $"Agent request exceeded {_agentTimeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds."
                });
        }
    }
}
