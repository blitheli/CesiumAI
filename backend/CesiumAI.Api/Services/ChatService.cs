using CesiumAI.Api.Models;

namespace CesiumAI.Api.Services;

public interface IChatService
{
    Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken);
}

public interface IAgentTurnRunner
{
    Task<string> RunAsync(
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

        string sessionId = string.IsNullOrWhiteSpace(request.SessionId)
            ? Guid.NewGuid().ToString()
            : request.SessionId;
        string prompt = _promptBuilder.Build(request);
        var collector = new SceneOpCollector();

        string message = await _agentTurnRunner.RunAsync(
            sessionId,
            prompt,
            collector,
            cancellationToken);

        return new ChatResponse(sessionId, message, collector.Drain());
    }
}
