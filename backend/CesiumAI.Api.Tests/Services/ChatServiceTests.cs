using CesiumAI.Api.Models;
using CesiumAI.Api.Services;
using FluentAssertions;

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

    private static ChatRequest Request(string? sessionId) =>
        new("hello", sessionId, new SceneSummary(null, []), null);

    private sealed class StubPromptBuilder(string prompt) : IScenePromptBuilder
    {
        public string Build(ChatRequest request) => prompt;
    }

    private sealed class RecordingTurnRunner(
        Func<string, string, SceneOpCollector, CancellationToken, Task<string>>? run = null)
        : IAgentTurnRunner
    {
        private readonly Func<string, string, SceneOpCollector, CancellationToken, Task<string>> _run =
            run ?? ((_, _, _, _) => Task.FromResult("agent response"));

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
    }
}
