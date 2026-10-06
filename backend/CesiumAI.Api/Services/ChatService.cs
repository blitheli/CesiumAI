using System.Runtime.CompilerServices;
using System.Text;
using CesiumAI.Api.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ChatResponse = CesiumAI.Api.Models.ChatResponse;

namespace CesiumAI.Api.Services;

public interface IChatService
{
    Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatRequest request, CancellationToken cancellationToken);
}

public interface IAgentTurnRunner
{
    Task<string> RunAsync(
        string sessionId,
        string prompt,
        SceneOpCollector collector,
        CancellationToken cancellationToken);

    IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        string sessionId,
        string prompt,
        SceneOpCollector collector,
        CancellationToken cancellationToken);
}

public sealed class ChatService(
    IScenePromptBuilder promptBuilder,
    IAgentTurnRunner agentTurnRunner) : IChatService
{
    private readonly IScenePromptBuilder _promptBuilder =
        promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
    private readonly IAgentTurnRunner _agentTurnRunner =
        agentTurnRunner ?? throw new ArgumentNullException(nameof(agentTurnRunner));

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string sessionId = ResolveSessionId(request);
        string prompt = _promptBuilder.Build(request);
        var collector = new SceneOpCollector();

        string message = await _agentTurnRunner.RunAsync(
            sessionId,
            prompt,
            collector,
            cancellationToken);

        return new ChatResponse(sessionId, message, collector.Drain());
    }

    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string sessionId = ResolveSessionId(request);
        string prompt = _promptBuilder.Build(request);
        var collector = new SceneOpCollector();
        var message = new StringBuilder();

        yield return new ChatSessionStreamEvent(sessionId);

        await foreach (AgentResponseUpdate update in _agentTurnRunner
            .RunStreamingAsync(sessionId, prompt, collector, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            foreach (AIContent content in update.Contents)
            {
                // 推理内容（TextReasoningContent）与审批等其他内容不下发给前端。
                switch (content)
                {
                    case TextContent { Text: { Length: > 0 } text }:
                        message.Append(text);
                        yield return new ChatDeltaStreamEvent(text);
                        break;
                    case FunctionCallContent call:
                        yield return new ChatToolCallStreamEvent(call.CallId, call.Name);
                        break;
                    case FunctionResultContent result:
                        yield return new ChatToolResultStreamEvent(result.CallId, result.Exception is null);
                        break;
                }
            }
        }

        yield return new ChatDoneStreamEvent(sessionId, message.ToString(), collector.Drain());
    }

    private static string ResolveSessionId(ChatRequest request) =>
        string.IsNullOrWhiteSpace(request.SessionId)
            ? Guid.NewGuid().ToString()
            : request.SessionId;
}
