using CesiumAI.Api.Models;
using CesiumAI.Api.Services;
using FluentAssertions;

namespace CesiumAI.Api.Tests.Services;

public class AgentRuntimeStoreTests
{
    [Fact]
    public async Task RunAsync_SerializesTurnsForTheSameSession()
    {
        var run = new ControlledRun();
        var factory = new FakeRuntimeFactory(run.InvokeAsync);
        var store = new AgentRuntimeStore(factory);

        Task<string> first = store.RunAsync("same", "first", new SceneOpCollector(), CancellationToken.None);
        await run.WaitForStartsAsync(1);

        Task<string> second = store.RunAsync("same", "second", new SceneOpCollector(), CancellationToken.None);

        run.StartCount.Should().Be(1);
        run.MaxActive.Should().Be(1);

        run.Release("first");
        (await first).Should().Be("reply:first");
        await run.WaitForStartsAsync(2);
        run.Release("second");
        (await second).Should().Be("reply:second");

        run.MaxActive.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_AllowsDifferentSessionsToOverlap()
    {
        var run = new ControlledRun();
        var factory = new FakeRuntimeFactory(run.InvokeAsync);
        var store = new AgentRuntimeStore(factory);

        Task<string> first = store.RunAsync("one", "first", new SceneOpCollector(), CancellationToken.None);
        Task<string> second = store.RunAsync("two", "second", new SceneOpCollector(), CancellationToken.None);
        await run.WaitForStartsAsync(2);

        run.MaxActive.Should().Be(2);

        run.Release("first");
        run.Release("second");
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task RunAsync_BindsEachTurnToOnlyItsCollector()
    {
        var factory = new FakeRuntimeFactory((sink, prompt, _) =>
        {
            sink.Add(new DeleteSceneOp([prompt]));
            return Task.FromResult($"reply:{prompt}");
        });
        var store = new AgentRuntimeStore(factory);
        var firstCollector = new SceneOpCollector();
        var secondCollector = new SceneOpCollector();

        await Task.WhenAll(
            store.RunAsync("same", "first", firstCollector, CancellationToken.None),
            store.RunAsync("same", "second", secondCollector, CancellationToken.None));

        firstCollector.Drain().Single().Should().BeEquivalentTo(new DeleteSceneOp(["first"]));
        secondCollector.Drain().Single().Should().BeEquivalentTo(new DeleteSceneOp(["second"]));
    }

    [Fact]
    public async Task RunAsync_CreatesRuntimeOncePerSessionId()
    {
        var factory = new FakeRuntimeFactory((_, prompt, _) => Task.FromResult(prompt));
        var store = new AgentRuntimeStore(factory);

        await store.RunAsync("same", "one", new SceneOpCollector(), CancellationToken.None);
        await store.RunAsync("same", "two", new SceneOpCollector(), CancellationToken.None);
        await store.RunAsync("other", "three", new SceneOpCollector(), CancellationToken.None);

        factory.CreateCounts.Should().BeEquivalentTo(
            new Dictionary<string, int>
            {
                ["same"] = 1,
                ["other"] = 1
            });
    }

    [Fact]
    public void TurnSceneOpSink_ThrowsWhenNoTurnIsActive()
    {
        var sink = new TurnSceneOpSink();

        Action act = () => sink.Add(new ClearSceneOp());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*active agent turn*");
    }

    [Fact]
    public async Task RunAsync_ClearsCollectorBindingWhenAgentThrows()
    {
        TurnSceneOpSink? createdSink = null;
        var expected = new InvalidOperationException("run failed");
        var factory = new FakeRuntimeFactory((sink, _, _) =>
        {
            createdSink = sink;
            return Task.FromException<string>(expected);
        });
        var store = new AgentRuntimeStore(factory);

        Func<Task> act = () => store.RunAsync("session", "prompt", new SceneOpCollector(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("run failed");
        Action addOutsideTurn = () => createdSink!.Add(new ClearSceneOp());
        addOutsideTurn.Should().Throw<InvalidOperationException>();
    }

    private sealed class FakeRuntimeFactory(
        Func<TurnSceneOpSink, string, CancellationToken, Task<string>> run) : IAgentRuntimeFactory
    {
        private readonly object _gate = new();

        public Dictionary<string, int> CreateCounts { get; } = [];

        public Task<AgentRuntime> CreateAsync(string sessionId, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                CreateCounts[sessionId] = CreateCounts.GetValueOrDefault(sessionId) + 1;
            }

            var sink = new TurnSceneOpSink();
            return Task.FromResult(new AgentRuntime(sink, (prompt, token) => run(sink, prompt, token)));
        }
    }

    private sealed class ControlledRun
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, TaskCompletionSource> _releases = [];
        private readonly TaskCompletionSource _firstStarted = NewCompletionSource();
        private readonly TaskCompletionSource _secondStarted = NewCompletionSource();
        private int _active;
        private int _maxActive;
        private int _startCount;

        public int MaxActive => Volatile.Read(ref _maxActive);
        public int StartCount => Volatile.Read(ref _startCount);

        public async Task<string> InvokeAsync(
            TurnSceneOpSink sink,
            string prompt,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource release;
            lock (_gate)
            {
                release = NewCompletionSource();
                _releases.Add(prompt, release);
            }

            int active = Interlocked.Increment(ref _active);
            UpdateMaximum(active);
            int start = Interlocked.Increment(ref _startCount);
            (start == 1 ? _firstStarted : _secondStarted).TrySetResult();

            try
            {
                await release.Task.WaitAsync(cancellationToken);
                return $"reply:{prompt}";
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public async Task WaitForStartsAsync(int count)
        {
            Task started = count switch
            {
                1 => _firstStarted.Task,
                2 => _secondStarted.Task,
                _ => throw new ArgumentOutOfRangeException(nameof(count))
            };

            await started.WaitAsync(TimeSpan.FromSeconds(2));
        }

        public void Release(string prompt)
        {
            TaskCompletionSource release;
            lock (_gate)
            {
                release = _releases[prompt];
            }

            release.TrySetResult();
        }

        private void UpdateMaximum(int candidate)
        {
            int current;
            do
            {
                current = Volatile.Read(ref _maxActive);
                if (candidate <= current)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref _maxActive, candidate, current) != current);
        }

        private static TaskCompletionSource NewCompletionSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
