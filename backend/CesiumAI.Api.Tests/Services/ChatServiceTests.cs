using System.Runtime.CompilerServices;
using CesiumAI.Api.Models;
using CesiumAI.Api.Services;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ChatResponse = CesiumAI.Api.Models.ChatResponse;

namespace CesiumAI.Api.Tests.Services;

public class ChatServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ChatAsync_CreatesGuidSessionId_WhenSessionIdIsMissingOrBlank(string? sessionId)
    {
        var runner = new RecordingTurnRunner();
        var service = new ChatService(new StubPromptBuilder("built prompt"), runner);

        ChatResponse response = await service.ChatAsync(Request(sessionId), CancellationToken.None);

        Guid.TryParse(response.SessionId, out _).Should().BeTrue();
        runner.SessionId.Should().Be(response.SessionId);
    }

    [Fact]
    public async Task ChatAsync_PreservesSessionId_AndPassesBuiltPrompt()
    {
        var runner = new RecordingTurnRunner();
        var service = new ChatService(new StubPromptBuilder("exact prompt"), runner);

        ChatResponse response = await service.ChatAsync(Request("existing-session"), CancellationToken.None);

        response.SessionId.Should().Be("existing-session");
        runner.SessionId.Should().Be("existing-session");
        runner.Prompt.Should().Be("exact prompt");
    }

    [Fact]
    public async Task ChatAsync_ReturnsAgentTextAndOnlyCollectedOperations()
    {
        var collectedOperation = new DeleteSceneOp(["entity-1"]);
        var runner = new RecordingTurnRunner((_, _, collector, _) =>
        {
            collector.Add(collectedOperation);
            return Task.FromResult("""assistant text containing {"id":"not-an-operation"}""");
        });
        var service = new ChatService(new StubPromptBuilder("prompt"), runner);

        ChatResponse response = await service.ChatAsync(Request("session"), CancellationToken.None);

        response.Message.Should().Be("""assistant text containing {"id":"not-an-operation"}""");
        response.SceneOps.Should().ContainSingle().Which.Should().BeSameAs(collectedOperation);
        runner.Collector!.Drain().Should().BeEmpty();
    }

    [Fact]
    public async Task ChatAsync_PropagatesAgentException_WithoutReturningAResponse()
    {
        var expected = new InvalidOperationException("agent failed");
        var runner = new RecordingTurnRunner((_, _, _, _) => Task.FromException<string>(expected));
        var service = new ChatService(new StubPromptBuilder("prompt"), runner);

        Func<Task> act = () => service.ChatAsync(Request("session"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("agent failed");
        runner.Collector!.Drain().Should().BeEmpty();
    }

    [Fact]
    public async Task StreamAsync_EmitsSessionDeltasToolEventsAndDoneInOrder()
    {
        var collectedOperation = new ClearSceneOp();
        var runner = new RecordingTurnRunner(runStreaming: (_, _, collector, _) => Updates(
            new AgentResponseUpdate(ChatRole.Assistant, "正在"),
            new AgentResponseUpdate(
                ChatRole.Assistant,
                [new FunctionCallContent("call-1", "ClearScene")]),
            new AgentResponseUpdate(
                ChatRole.Tool,
                [new FunctionResultContent("call-1", "Scene clear queued.")]),
            new AgentResponseUpdate(
                ChatRole.Assistant,
                [new TextReasoningContent("内部推理不下发"), new TextContent("清空。")]),
            new AgentResponseUpdate(ChatRole.Assistant, string.Empty))
            .WithSideEffect(() => collector.Add(collectedOperation)));
        var service = new ChatService(new StubPromptBuilder("prompt"), runner);

        List<ChatStreamEvent> events = await Collect(
            service.StreamAsync(Request("session-1"), CancellationToken.None));

        events.Should().Equal(
            new ChatSessionStreamEvent("session-1"),
            new ChatDeltaStreamEvent("正在"),
            new ChatToolCallStreamEvent("call-1", "ClearScene"),
            new ChatToolResultStreamEvent("call-1", Succeeded: true),
            new ChatDeltaStreamEvent("清空。"),
            events[^1]);
        ChatDoneStreamEvent done = events[^1].Should().BeOfType<ChatDoneStreamEvent>().Subject;
        done.SessionId.Should().Be("session-1");
        done.Message.Should().Be("正在清空。");
        done.SceneOps.Should().ContainSingle().Which.Should().BeSameAs(collectedOperation);
        runner.Prompt.Should().Be("prompt");
    }

    [Fact]
    public async Task StreamAsync_ReportsFailedToolResult()
    {
        var runner = new RecordingTurnRunner(runStreaming: (_, _, _, _) => Updates(
            new AgentResponseUpdate(
                ChatRole.Tool,
                [new FunctionResultContent("call-1", "error") { Exception = new InvalidOperationException() }])));
        var service = new ChatService(new StubPromptBuilder("prompt"), runner);

        List<ChatStreamEvent> events = await Collect(
            service.StreamAsync(Request("session-1"), CancellationToken.None));

        events.Should().Contain(new ChatToolResultStreamEvent("call-1", Succeeded: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task StreamAsync_CreatesGuidSessionId_WhenSessionIdIsMissingOrBlank(string? sessionId)
    {
        var runner = new RecordingTurnRunner();
        var service = new ChatService(new StubPromptBuilder("prompt"), runner);

        List<ChatStreamEvent> events = await Collect(
            service.StreamAsync(Request(sessionId), CancellationToken.None));

        string created = events[0].Should().BeOfType<ChatSessionStreamEvent>().Subject.SessionId;
        Guid.TryParse(created, out _).Should().BeTrue();
        runner.SessionId.Should().Be(created);
        events[^1].Should().BeOfType<ChatDoneStreamEvent>().Which.SessionId.Should().Be(created);
    }

    [Fact]
    public async Task StreamAsync_PropagatesAgentException_WithoutDoneEvent()
    {
        var runner = new RecordingTurnRunner(runStreaming: (_, _, collector, _) => Failing(collector));
        var service = new ChatService(new StubPromptBuilder("prompt"), runner);
        var events = new List<ChatStreamEvent>();

        Func<Task> act = async () =>
        {
            await foreach (ChatStreamEvent streamEvent in service.StreamAsync(
                Request("session"),
                CancellationToken.None))
            {
                events.Add(streamEvent);
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("agent failed");
        events.Should().NotContain(streamEvent => streamEvent is ChatDoneStreamEvent);

        static async IAsyncEnumerable<AgentResponseUpdate> Failing(SceneOpCollector collector)
        {
            await Task.Yield();
            collector.Add(new ClearSceneOp());
            yield return new AgentResponseUpdate(ChatRole.Assistant, "部分");
            throw new InvalidOperationException("agent failed");
        }
    }

    private static async Task<List<ChatStreamEvent>> Collect(IAsyncEnumerable<ChatStreamEvent> source)
    {
        var events = new List<ChatStreamEvent>();
        await foreach (ChatStreamEvent streamEvent in source)
        {
            events.Add(streamEvent);
        }

        return events;
    }

    private static async IAsyncEnumerable<AgentResponseUpdate> Updates(params AgentResponseUpdate[] updates)
    {
        foreach (AgentResponseUpdate update in updates)
        {
            await Task.Yield();
            yield return update;
        }
    }

    private static ChatRequest Request(string? sessionId) =>
        new("hello", sessionId, new SceneSummary(null, []), null);

    private sealed class StubPromptBuilder(string prompt) : IScenePromptBuilder
    {
        public string Build(ChatRequest request) => prompt;
    }

    private sealed class RecordingTurnRunner(
        Func<string, string, SceneOpCollector, CancellationToken, Task<string>>? run = null,
        Func<string, string, SceneOpCollector, CancellationToken, IAsyncEnumerable<AgentResponseUpdate>>? runStreaming = null)
        : IAgentTurnRunner
    {
        private readonly Func<string, string, SceneOpCollector, CancellationToken, Task<string>> _run =
            run ?? ((_, _, _, _) => Task.FromResult("agent response"));
        private readonly Func<string, string, SceneOpCollector, CancellationToken, IAsyncEnumerable<AgentResponseUpdate>> _runStreaming =
            runStreaming ?? ((_, _, _, _) => Updates(new AgentResponseUpdate(ChatRole.Assistant, "agent response")));

        public string? SessionId { get; private set; }
        public string? Prompt { get; private set; }
        public SceneOpCollector? Collector { get; private set; }

        public Task<string> RunAsync(
            string sessionId,
            string prompt,
            SceneOpCollector collector,
            CancellationToken cancellationToken)
        {
            SessionId = sessionId;
            Prompt = prompt;
            Collector = collector;
            return _run(sessionId, prompt, collector, cancellationToken);
        }

        public IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
            string sessionId,
            string prompt,
            SceneOpCollector collector,
            CancellationToken cancellationToken)
        {
            SessionId = sessionId;
            Prompt = prompt;
            Collector = collector;
            return _runStreaming(sessionId, prompt, collector, cancellationToken);
        }
    }
}

internal static class AsyncEnumerableTestExtensions
{
    /// <summary>在源序列全部产出后执行副作用，模拟工具在流中途写入 collector。</summary>
    public static async IAsyncEnumerable<T> WithSideEffect<T>(
        this IAsyncEnumerable<T> source,
        Action sideEffect,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (T item in source.WithCancellation(cancellationToken))
        {
            yield return item;
        }

        sideEffect();
    }
}
