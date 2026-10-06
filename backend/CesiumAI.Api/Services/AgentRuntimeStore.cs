using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using CesiumAI.Api.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

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
    private readonly Func<string, CancellationToken, IAsyncEnumerable<AgentResponseUpdate>> _runStreamingAsync;

    /// <summary>
    /// 测试用构造：未提供流式委托时，把非流式结果包装为单个文本更新。
    /// </summary>
    public AgentRuntime(
        TurnSceneOpSink sceneOpSink,
        Func<string, CancellationToken, Task<string>> runAsync,
        Func<string, CancellationToken, IAsyncEnumerable<AgentResponseUpdate>>? runStreamingAsync = null)
    {
        SceneOpSink = sceneOpSink ?? throw new ArgumentNullException(nameof(sceneOpSink));
        _runAsync = runAsync ?? throw new ArgumentNullException(nameof(runAsync));
        _runStreamingAsync = runStreamingAsync ?? WrapAsSingleUpdate(runAsync);
    }

    public AgentRuntime(AIAgent agent, AgentSession session, TurnSceneOpSink sceneOpSink)
    {
        Agent = agent ?? throw new ArgumentNullException(nameof(agent));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        SceneOpSink = sceneOpSink ?? throw new ArgumentNullException(nameof(sceneOpSink));
        _runAsync = RunAgentAsync;
        _runStreamingAsync = RunAgentStreamingAsync;
    }

    internal AIAgent? Agent { get; }

    internal AgentSession? Session { get; }

    internal TurnSceneOpSink SceneOpSink { get; }

    internal SemaphoreSlim TurnSemaphore { get; } = new(1, 1);

    internal Task<string> RunAsync(string prompt, CancellationToken cancellationToken) =>
        _runAsync(prompt, cancellationToken);

    internal IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        string prompt,
        CancellationToken cancellationToken) =>
        _runStreamingAsync(prompt, cancellationToken);

    private async Task<string> RunAgentAsync(string prompt, CancellationToken cancellationToken)
    {
        AgentResponse response = await Agent!.RunAsync(
            prompt,
            Session,
            cancellationToken: cancellationToken);

        return response.Text;
    }

    private IAsyncEnumerable<AgentResponseUpdate> RunAgentStreamingAsync(
        string prompt,
        CancellationToken cancellationToken) =>
        Agent!.RunStreamingAsync(prompt, Session, cancellationToken: cancellationToken);

    private static Func<string, CancellationToken, IAsyncEnumerable<AgentResponseUpdate>> WrapAsSingleUpdate(
        Func<string, CancellationToken, Task<string>> runAsync) =>
        (prompt, cancellationToken) => SingleUpdateAsync(runAsync, prompt, cancellationToken);

    private static async IAsyncEnumerable<AgentResponseUpdate> SingleUpdateAsync(
        Func<string, CancellationToken, Task<string>> runAsync,
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string text = await runAsync(prompt, cancellationToken);
        yield return new AgentResponseUpdate(ChatRole.Assistant, text);
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
        ValidateTurnArguments(sessionId, prompt, collector);

        AgentRuntime runtime = await GetRuntimeAsync(sessionId, cancellationToken);
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

    /// <summary>
    /// 流式执行一轮。会话锁与 collector 绑定覆盖整个枚举过程，
    /// 直到调用方枚举结束或释放枚举器（含异常、取消）才解除。
    /// </summary>
    public async IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        string sessionId,
        string prompt,
        SceneOpCollector collector,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ValidateTurnArguments(sessionId, prompt, collector);

        AgentRuntime runtime = await GetRuntimeAsync(sessionId, cancellationToken);
        await runtime.TurnSemaphore.WaitAsync(cancellationToken);

        try
        {
            runtime.SceneOpSink.Current = collector;
            await foreach (AgentResponseUpdate update in runtime
                .RunStreamingAsync(prompt, cancellationToken)
                .WithCancellation(cancellationToken))
            {
                yield return update;
            }
        }
        finally
        {
            runtime.SceneOpSink.Current = null;
            runtime.TurnSemaphore.Release();
        }
    }

    private static void ValidateTurnArguments(
        string sessionId,
        string prompt,
        SceneOpCollector collector)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session id cannot be blank.", nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(collector);
    }

    private Task<AgentRuntime> GetRuntimeAsync(string sessionId, CancellationToken cancellationToken)
    {
        Lazy<Task<AgentRuntime>> lazyRuntime = _runtimes.GetOrAdd(
            sessionId,
            id => new Lazy<Task<AgentRuntime>>(
                () => _runtimeFactory.CreateAsync(id, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyRuntime.Value.WaitAsync(cancellationToken);
    }
}
