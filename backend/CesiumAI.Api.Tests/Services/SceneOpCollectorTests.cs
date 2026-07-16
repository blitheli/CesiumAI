using CesiumAI.Api.Models;
using CesiumAI.Api.Services;
using FluentAssertions;

namespace CesiumAI.Api.Tests.Services;

public class SceneOpCollectorTests
{
    [Fact]
    public void Drain_ReturnsQueuedOperations_AndClearsCollector()
    {
        var collector = new SceneOpCollector();

        collector.Add(new ClearSceneOp());

        collector.Drain().Should().ContainSingle().Which.Should().BeOfType<ClearSceneOp>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public async Task Drain_ReturnsAllParallelAdds_ExactlyOnce()
    {
        var collector = new SceneOpCollector();

        await Task.WhenAll(
            Enumerable.Range(1, 20)
                .Select(index => Task.Run(() => collector.Add(new DeleteSceneOp([$"id-{index}"])))));

        IReadOnlyList<SceneOp> drained = collector.Drain();

        drained.Should().HaveCount(20);
        drained.Should().AllBeOfType<DeleteSceneOp>();
        drained
            .Cast<DeleteSceneOp>()
            .SelectMany(operation => operation.Ids)
            .Should()
            .BeEquivalentTo(Enumerable.Range(1, 20).Select(index => $"id-{index}"));
        collector.Drain().Should().BeEmpty();
    }
}
