using System.Collections.Concurrent;
using CesiumAI.Api.Models;
using Microsoft.Agents.AI;

namespace CesiumAI.Api.Services;

public interface IAgentRuntimeFactory
{
    Task<AgentRuntime> CreateAsync(string sessionId, CancellationToken cancellationToken);
}

public sealed class TurnSceneOpSink : ISceneOpSink
{
    internal SceneOpCollector? Current { get; set; }

    public void Add(SceneOp operation)
    {
        SceneOpCollector collector = Current
            ?? throw new InvalidOperationException("Scene operations require an active agent turn.");

        collector.Add(operation);
    }
}

public sealed class AgentRuntime
{
    private readonly Func<string, CancellationToken, Task<string>> _runAsync;

    public AgentRuntime(
        TurnSceneOpSink sceneOpSink,
        Func<string, CancellationToken, Task<string>> runAsync)
    {
        SceneOpSink = sceneOpSink ?? throw new ArgumentNullException(nameof(sceneOpSink));
        _runAsync = runAsync ?? throw new ArgumentNullException(nameof(runAsync));
    }

    public AgentRuntime(AIAgent agent, AgentSession session, TurnSceneOpSink sceneOpSink)
    {
        Agent = agent ?? throw new ArgumentNullException(nameof(agent));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        SceneOpSink = sceneOpSink ?? throw new ArgumentNullException(nameof(sceneOpSink));
        _runAsync = RunAgentAsync;
    }

    internal AIAgent? Agent { get; }

    internal AgentSession? Session { get; }

    internal TurnSceneOpSink SceneOpSink { get; }

    internal SemaphoreSlim TurnSemaphore { get; } = new(1, 1);

    internal Task<string> RunAsync(string prompt, CancellationToken cancellationToken) =>
        _runAsync(prompt, cancellationToken);

    private async Task<string> RunAgentAsync(string prompt, CancellationToken cancellationToken)
    {
        AgentResponse response = await Agent!.RunAsync(
            prompt,
            Session,
            cancellationToken: cancellationToken);

        return response.Text;
    }
}

public sealed class AgentRuntimeStore(IAgentRuntimeFactory runtimeFactory) : IAgentTurnRunner
{
    private readonly IAgentRuntimeFactory _runtimeFactory =
        runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
    private readonly ConcurrentDictionary<string, Lazy<Task<AgentRuntime>>> _runtimes = new();

    public async Task<string> RunAsync(
        string sessionId,
        string prompt,
        SceneOpCollector collector,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session id cannot be blank.", nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(collector);

        Lazy<Task<AgentRuntime>> lazyRuntime = _runtimes.GetOrAdd(
            sessionId,
            id => new Lazy<Task<AgentRuntime>>(
                () => _runtimeFactory.CreateAsync(id, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));

        AgentRuntime runtime = await lazyRuntime.Value.WaitAsync(cancellationToken);
        await runtime.TurnSemaphore.WaitAsync(cancellationToken);

        try
        {
            runtime.SceneOpSink.Current = collector;
            return await runtime.RunAsync(prompt, cancellationToken);
        }
        finally
        {
            runtime.SceneOpSink.Current = null;
            runtime.TurnSemaphore.Release();
        }
    }
}
