using CesiumAI.Api.Models;
using CesiumAI.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CesiumAI.Api.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController(IChatService chatService) : ControllerBase
{
    private static readonly TimeSpan AgentTimeout = TimeSpan.FromSeconds(120);
    private readonly IChatService _chatService =
        chatService ?? throw new ArgumentNullException(nameof(chatService));

    [HttpPost]
    [ProducesResponseType<ChatResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status499ClientClosedRequest)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<ChatResponse>> Post(ChatRequest request)
    {
        CancellationToken requestAborted = HttpContext.RequestAborted;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        timeout.CancelAfter(AgentTimeout);

        try
        {
            ChatResponse response = await _chatService.ChatAsync(request, timeout.Token);
            return Ok(response);
        }
        catch (OperationCanceledException) when (requestAborted.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(
                StatusCodes.Status504GatewayTimeout,
                new
                {
                    error = "agent_timeout",
                    detail = "Agent request exceeded 120 seconds."
                });
        }
    }
}
