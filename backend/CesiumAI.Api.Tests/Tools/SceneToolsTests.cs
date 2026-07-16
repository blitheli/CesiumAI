using System.Text.Json;
using CesiumAI.Api.Astrox;
using CesiumAI.Api.Models;
using CesiumAI.Api.Services;
using CesiumAI.Api.Tools;
using FluentAssertions;

namespace CesiumAI.Api.Tests.Tools;

public class SceneToolsTests
{
    [Fact]
    public void ClearScene_QueuesClearSceneOperation()
    {
        var collector = new SceneOpCollector();
        var tools = new SceneTools(collector, new StubOrbitScenarioService());

        string result = tools.ClearScene();

        result.Should().NotBeNullOrWhiteSpace();
        collector.Drain().Should().ContainSingle().Which.Should().BeOfType<ClearSceneOp>();
    }

    [Fact]
    public void UpsertFacility_QueuesCompleteFacilityPacket()
    {
        var collector = new SceneOpCollector();
        var tools = new SceneTools(collector, new StubOrbitScenarioService());

        string result = tools.UpsertFacility("sanya", "三亚", 109.5, 18.2, 50);

        result.Should().Contain("sanya");
        IReadOnlyList<SceneOp> operations = collector.Drain();
        operations.Should().ContainSingle();

        UpsertSceneOp operation = operations.Single().Should().BeOfType<UpsertSceneOp>().Subject;
        operation.Packets.Should().ContainSingle();
        JsonElement packet = operation.Packets.Single();

        packet.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["id", "name", "position", "point", "label"]);
        packet.GetProperty("id").GetString().Should().Be("sanya");
        packet.GetProperty("name").GetString().Should().Be("三亚");
        packet.GetProperty("position").GetProperty("cartographicDegrees")
            .EnumerateArray()
            .Select(value => value.GetDouble())
            .Should()
            .Equal([109.5, 18.2, 50]);

        JsonElement point = packet.GetProperty("point");
        point.GetProperty("pixelSize").GetInt32().Should().Be(10);
        point.GetProperty("color").GetProperty("rgba").EnumerateArray().Select(value => value.GetInt32()).Should().Equal([255, 80, 80, 255]);
        point.GetProperty("outlineColor").GetProperty("rgba").EnumerateArray().Select(value => value.GetInt32()).Should().Equal([255, 255, 255, 255]);
        point.GetProperty("outlineWidth").GetInt32().Should().Be(2);

        JsonElement label = packet.GetProperty("label");
        label.GetProperty("text").GetString().Should().Be("三亚");
        label.GetProperty("show").GetBoolean().Should().BeTrue();
        label.GetProperty("pixelOffset").GetProperty("cartesian2").EnumerateArray().Select(value => value.GetInt32()).Should().Equal([0, -18]);
    }

    [Theory]
    [InlineData(180.1, 0)]
    [InlineData(-180.1, 0)]
    [InlineData(0, 90.1)]
    [InlineData(0, -90.1)]
    public void UpsertFacility_RejectsOutOfRangeCoordinates_WithoutQueuingOperations(
        double longitudeDegrees,
        double latitudeDegrees)
    {
        var collector = new SceneOpCollector();
        var tools = new SceneTools(collector, new StubOrbitScenarioService());

        Action act = () => tools.UpsertFacility("sanya", "三亚", longitudeDegrees, latitudeDegrees);

        act.Should().Throw<ArgumentOutOfRangeException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void UpsertFacility_RejectsBlankId_WithoutQueuingOperations()
    {
        var collector = new SceneOpCollector();
        var tools = new SceneTools(collector, new StubOrbitScenarioService());

        Action act = () => tools.UpsertFacility(" ", "三亚", 109.5, 18.2);

        act.Should().Throw<ArgumentException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void DeleteEntity_FiltersDocumentBlankAndDuplicateIds()
    {
        var collector = new SceneOpCollector();
        var tools = new SceneTools(collector, new StubOrbitScenarioService());

        string result = tools.DeleteEntity(["a", "a", "document", " "]);

        result.Should().Contain("a");
        IReadOnlyList<SceneOp> operations = collector.Drain();
        operations.Should().ContainSingle();

        DeleteSceneOp operation = operations.Single().Should().BeOfType<DeleteSceneOp>().Subject;
        operation.Ids.Should().Equal("a");
    }

    [Fact]
    public async Task AddSatelliteJ2_QueuesReturnedPacket_AndUsesDefaultScenarioValues()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-16T10:08:59Z");
        JsonElement expectedPacket = JsonSerializer.SerializeToElement(new
        {
            id = "sat-1",
            name = "sat-1",
            availability = "2026-07-16T10:08:00.000Z/2026-07-17T10:08:00.000Z",
            position = new
            {
                cartesianVelocity = new
                {
                    epoch = "2026-07-16T10:08:00.000Z",
                    cartesian = new[] { 0, 1, 2, 3, 4, 5 }
                }
            },
            point = new
            {
                pixelSize = 8,
                color = new
                {
                    rgba = new[] { 255, 220, 0, 255 }
                }
            },
            path = new
            {
                show = true,
                width = 2,
                leadTime = 0,
                trailTime = 86400
            },
            properties = new
            {
                orbitHint = new
                {
                    @string = "900 km SSO / J2"
                }
            }
        });
        var orbitService = new StubOrbitScenarioService((_, _) => Task.FromResult(expectedPacket));
        var collector = new SceneOpCollector();
        var tools = new SceneTools(collector, orbitService, new FixedTimeProvider(now));

        string result = await tools.AddSatelliteJ2("sat-1");

        result.Should().Contain("sat-1");
        orbitService.Scenarios.Should().ContainSingle();

        SsoJ2Scenario scenario = orbitService.Scenarios.Single();
        scenario.Id.Should().Be("sat-1");
        scenario.Name.Should().Be("sat-1");
        scenario.AltitudeKm.Should().Be(900);
        scenario.Hours.Should().Be(24);
        scenario.StepSeconds.Should().Be(60);
        scenario.LocalTimeOfDescendingNode.Should().Be(10.5);
        scenario.EpochUtc.Should().Be(DateTimeOffset.Parse("2026-07-16T10:08:00Z"));

        IReadOnlyList<SceneOp> operations = collector.Drain();
        operations.Should().ContainSingle();

        UpsertSceneOp operation = operations.Single().Should().BeOfType<UpsertSceneOp>().Subject;
        operation.Packets.Should().ContainSingle();
        operation.Packets.Single().GetRawText().Should().Be(expectedPacket.GetRawText());
    }

    [Fact]
    public async Task AddSatelliteJ2_DoesNotQueueOperations_WhenOrbitServiceThrowsAstroxException()
    {
        var orbitService = new StubOrbitScenarioService((_, _) =>
            throw new AstroxException("astrox unavailable"));
        var collector = new SceneOpCollector();
        var tools = new SceneTools(collector, orbitService, new FixedTimeProvider(DateTimeOffset.Parse("2026-07-16T10:08:59Z")));

        Func<Task> act = async () => await tools.AddSatelliteJ2("sat-1");

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("astrox unavailable");
        collector.Drain().Should().BeEmpty();
    }

    private sealed class StubOrbitScenarioService(
        Func<SsoJ2Scenario, CancellationToken, Task<JsonElement>>? createPacket = null) : IOrbitScenarioService
    {
        private readonly Func<SsoJ2Scenario, CancellationToken, Task<JsonElement>> _createPacket =
            createPacket ?? ((_, _) => Task.FromResult(JsonSerializer.SerializeToElement(new { id = "default-sat" })));

        public List<SsoJ2Scenario> Scenarios { get; } = [];

        public Task<JsonElement> CreateSsoJ2PacketAsync(SsoJ2Scenario scenario, CancellationToken cancellationToken)
        {
            Scenarios.Add(scenario);
            return _createPacket(scenario, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
