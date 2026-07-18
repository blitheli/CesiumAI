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
            ["id", "name", "position", "point", "label", "model"]);
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

        JsonElement model = packet.GetProperty("model");
        model.GetProperty("gltf").GetString().Should().Be("/models/facility.glb");
        model.GetProperty("minimumPixelSize").GetInt32().Should().Be(64);
        model.GetProperty("maximumScale").GetInt32().Should().Be(20000);
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

    [Fact]
    public void FocusEntity_QueuesSingleCameraFocusOperation()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        string result = tools.FocusEntity("iss", distanceMeters: 2_000_000, headingDegrees: 15, pitchDegrees: -30);

        result.Should().Contain("iss");
        CameraSceneOp operation = collector.Drain().Should().ContainSingle().Which.Should().BeOfType<CameraSceneOp>().Subject;
        operation.Action.Should().Be(CameraAction.Focus);
        operation.TargetId.Should().Be("iss");
        operation.DistanceMeters.Should().Be(2_000_000);
        operation.HeadingDegrees.Should().Be(15);
        operation.PitchDegrees.Should().Be(-30);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("document")]
    [InlineData("DOCUMENT")]
    public void FocusEntity_RejectsBlankOrDocumentId_WithoutQueuingOperations(string id)
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action act = () => tools.FocusEntity(id);

        act.Should().Throw<ArgumentException>();
        collector.Drain().Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void FocusEntity_RejectsNonPositiveDistance_WithoutQueuingOperations(double distanceMeters)
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action act = () => tools.FocusEntity("iss", distanceMeters: distanceMeters);

        act.Should().Throw<ArgumentOutOfRangeException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void FocusEntity_RejectsNonFiniteNumericParameters_WithoutQueuingOperations()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action distanceAct = () => tools.FocusEntity("iss", distanceMeters: double.NaN);
        Action headingAct = () => tools.FocusEntity("iss", headingDegrees: double.PositiveInfinity);
        Action pitchAct = () => tools.FocusEntity("iss", pitchDegrees: double.NegativeInfinity);

        distanceAct.Should().Throw<ArgumentOutOfRangeException>();
        headingAct.Should().Throw<ArgumentOutOfRangeException>();
        pitchAct.Should().Throw<ArgumentOutOfRangeException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void TrackEntity_QueuesSingleCameraTrackOperation()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        string result = tools.TrackEntity("iss");

        result.Should().Contain("iss");
        CameraSceneOp operation = collector.Drain().Should().ContainSingle().Which.Should().BeOfType<CameraSceneOp>().Subject;
        operation.Action.Should().Be(CameraAction.Track);
        operation.TargetId.Should().Be("iss");
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("document")]
    public void TrackEntity_RejectsBlankOrDocumentId_WithoutQueuingOperations(string id)
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action act = () => tools.TrackEntity(id);

        act.Should().Throw<ArgumentException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void StopTracking_QueuesSingleCameraUntrackOperation()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        string result = tools.StopTracking();

        result.Should().NotBeNullOrWhiteSpace();
        CameraSceneOp operation = collector.Drain().Should().ContainSingle().Which.Should().BeOfType<CameraSceneOp>().Subject;
        operation.Action.Should().Be(CameraAction.Untrack);
    }

    [Fact]
    public void AdjustCamera_Zoom_QueuesSingleZoomOperation()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        string result = tools.AdjustCamera(action: "zoom", amount: 500);

        result.Should().NotBeNullOrWhiteSpace();
        CameraSceneOp operation = collector.Drain().Should().ContainSingle().Which.Should().BeOfType<CameraSceneOp>().Subject;
        operation.Action.Should().Be(CameraAction.Zoom);
        operation.Amount.Should().Be(500);
    }

    [Fact]
    public void AdjustCamera_Zoom_RejectsZeroAmount_WithoutQueuingOperations()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action act = () => tools.AdjustCamera(action: "zoom", amount: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void AdjustCamera_Zoom_RejectsNonFiniteAmount_WithoutQueuingOperations()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action nanAct = () => tools.AdjustCamera(action: "zoom", amount: double.NaN);
        Action infinityAct = () => tools.AdjustCamera(action: "zoom", amount: double.PositiveInfinity);

        nanAct.Should().Throw<ArgumentOutOfRangeException>();
        infinityAct.Should().Throw<ArgumentOutOfRangeException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void AdjustCamera_Zoom_RejectsNonFiniteInapplicableParameters_WithoutQueuingOperations()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action act = () => tools.AdjustCamera(
            action: "zoom",
            amount: 500,
            headingDegrees: double.NaN,
            pitchDegrees: double.PositiveInfinity,
            rollDegrees: double.NegativeInfinity);

        act.Should().Throw<ArgumentOutOfRangeException>();
        collector.Drain().Should().BeEmpty();
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("up")]
    [InlineData("down")]
    public void AdjustCamera_Pan_QueuesSinglePanOperation(string direction)
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        string result = tools.AdjustCamera(action: "pan", direction: direction, amount: 100);

        result.Should().NotBeNullOrWhiteSpace();
        CameraSceneOp operation = collector.Drain().Should().ContainSingle().Which.Should().BeOfType<CameraSceneOp>().Subject;
        operation.Action.Should().Be(CameraAction.Pan);
        operation.Direction.Should().Be(direction);
        operation.Amount.Should().Be(100);
    }

    [Theory]
    [InlineData("forward")]
    [InlineData("")]
    [InlineData("LEFT")]
    public void AdjustCamera_Pan_RejectsInvalidDirection_WithoutQueuingOperations(string direction)
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action act = () => tools.AdjustCamera(action: "pan", direction: direction, amount: 100);

        act.Should().Throw<ArgumentException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void AdjustCamera_Pan_RejectsNonFiniteAmount_WithoutQueuingOperations()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action act = () => tools.AdjustCamera(action: "pan", direction: "left", amount: double.NaN);

        act.Should().Throw<ArgumentOutOfRangeException>();
        collector.Drain().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void AdjustCamera_Pan_RejectsNonPositiveAmount_WithoutQueuingOperations(double? amount)
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action act = () => tools.AdjustCamera(action: "pan", direction: "left", amount: amount);

        act.Should().Throw<ArgumentOutOfRangeException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void AdjustCamera_Rotate_QueuesSingleRotateOperation()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        string result = tools.AdjustCamera(
            action: "rotate",
            headingDegrees: 30,
            pitchDegrees: -10,
            rollDegrees: 5);

        result.Should().NotBeNullOrWhiteSpace();
        CameraSceneOp operation = collector.Drain().Should().ContainSingle().Which.Should().BeOfType<CameraSceneOp>().Subject;
        operation.Action.Should().Be(CameraAction.Rotate);
        operation.HeadingDegrees.Should().Be(30);
        operation.PitchDegrees.Should().Be(-10);
        operation.RollDegrees.Should().Be(5);
    }

    [Fact]
    public void AdjustCamera_Description_DocumentsPositiveRightNegativeLeftHeading()
    {
        System.ComponentModel.DescriptionAttribute? description =
            typeof(SceneTools)
                .GetMethod(nameof(SceneTools.AdjustCamera))
                ?.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), inherit: false)
                .Cast<System.ComponentModel.DescriptionAttribute>()
                .SingleOrDefault();

        description.Should().NotBeNull();
        description!.Description.Should().Contain("正").And.Contain("右");
        description.Description.Should().Contain("负").And.Contain("左");
        description.Description.Should().Contain("headingDegrees");
    }

    [Fact]
    public void AdjustCamera_Rotate_RejectsNonFiniteAnglesOrAmount_WithoutQueuingOperations()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action headingAct = () => tools.AdjustCamera(action: "rotate", headingDegrees: double.NaN);
        Action pitchAct = () => tools.AdjustCamera(action: "rotate", pitchDegrees: double.PositiveInfinity);
        Action rollAct = () => tools.AdjustCamera(action: "rotate", rollDegrees: double.NegativeInfinity);
        Action amountAct = () => tools.AdjustCamera(action: "rotate", amount: double.NaN);

        headingAct.Should().Throw<ArgumentOutOfRangeException>();
        pitchAct.Should().Throw<ArgumentOutOfRangeException>();
        rollAct.Should().Throw<ArgumentOutOfRangeException>();
        amountAct.Should().Throw<ArgumentOutOfRangeException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void AdjustCamera_Rotate_RejectsAllZeroOrMissingAngles_WithoutQueuingOperations()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action missing = () => tools.AdjustCamera(action: "rotate");
        Action allZero = () => tools.AdjustCamera(
            action: "rotate",
            headingDegrees: 0,
            pitchDegrees: 0,
            rollDegrees: 0);

        missing.Should().Throw<ArgumentException>();
        allZero.Should().Throw<ArgumentException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void OrbitEntity_Step_QueuesSingleOrbitStepOperation()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        string result = tools.OrbitEntity(
            id: "iss",
            mode: "step",
            amount: 45,
            headingDegrees: 10,
            pitchDegrees: -20,
            distanceMeters: 1_500_000);

        result.Should().Contain("iss");
        CameraSceneOp operation = collector.Drain().Should().ContainSingle().Which.Should().BeOfType<CameraSceneOp>().Subject;
        operation.Action.Should().Be(CameraAction.OrbitStep);
        operation.TargetId.Should().Be("iss");
        operation.Amount.Should().Be(45);
        operation.HeadingDegrees.Should().Be(10);
        operation.PitchDegrees.Should().Be(-20);
        operation.DistanceMeters.Should().Be(1_500_000);
    }

    [Fact]
    public void OrbitEntity_Start_QueuesSingleOrbitStartOperation()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        string result = tools.OrbitEntity(
            id: "iss",
            mode: "start",
            angularSpeedDegreesPerSecond: 12);

        result.Should().Contain("iss");
        CameraSceneOp operation = collector.Drain().Should().ContainSingle().Which.Should().BeOfType<CameraSceneOp>().Subject;
        operation.Action.Should().Be(CameraAction.OrbitStart);
        operation.TargetId.Should().Be("iss");
        operation.AngularSpeedDegreesPerSecond.Should().Be(12);
    }

    [Theory]
    [InlineData("loop")]
    [InlineData("")]
    [InlineData("STEP")]
    public void OrbitEntity_RejectsInvalidMode_WithoutQueuingOperations(string mode)
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action act = () => tools.OrbitEntity(id: "iss", mode: mode, amount: 45);

        act.Should().Throw<ArgumentException>();
        collector.Drain().Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void OrbitEntity_Start_RejectsNonPositiveAngularSpeed_WithoutQueuingOperations(double angularSpeed)
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action act = () => tools.OrbitEntity(
            id: "iss",
            mode: "start",
            angularSpeedDegreesPerSecond: angularSpeed);

        act.Should().Throw<ArgumentOutOfRangeException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void OrbitEntity_Step_RejectsMissingZeroOrNonFiniteAmount_WithoutQueuingOperations()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action missingAct = () => tools.OrbitEntity(id: "iss", mode: "step");
        Action zeroAct = () => tools.OrbitEntity(id: "iss", mode: "step", amount: 0);
        Action nanAct = () => tools.OrbitEntity(id: "iss", mode: "step", amount: double.NaN);
        Action infinityAct = () => tools.OrbitEntity(id: "iss", mode: "step", amount: double.PositiveInfinity);

        missingAct.Should().Throw<ArgumentOutOfRangeException>();
        zeroAct.Should().Throw<ArgumentOutOfRangeException>();
        nanAct.Should().Throw<ArgumentOutOfRangeException>();
        infinityAct.Should().Throw<ArgumentOutOfRangeException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void OrbitEntity_Start_RejectsNonFiniteAngularSpeed_WithoutQueuingOperations()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action nanAct = () => tools.OrbitEntity(
            id: "iss",
            mode: "start",
            angularSpeedDegreesPerSecond: double.NaN);
        Action infinityAct = () => tools.OrbitEntity(
            id: "iss",
            mode: "start",
            angularSpeedDegreesPerSecond: double.PositiveInfinity);

        nanAct.Should().Throw<ArgumentOutOfRangeException>();
        infinityAct.Should().Throw<ArgumentOutOfRangeException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void OrbitEntity_RejectsNonFiniteInapplicableOrOptionalParameters_WithoutQueuingOperations()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        // 仅注入当前 mode 不适用或可选字段上的非有限值，避免被 distance 校验提前挡住。
        Action stepAngularAct = () => tools.OrbitEntity(
            id: "iss",
            mode: "step",
            amount: 45,
            angularSpeedDegreesPerSecond: double.NaN);
        Action stepHeadingAct = () => tools.OrbitEntity(
            id: "iss",
            mode: "step",
            amount: 45,
            headingDegrees: double.PositiveInfinity);
        Action stepPitchAct = () => tools.OrbitEntity(
            id: "iss",
            mode: "step",
            amount: 45,
            pitchDegrees: double.NegativeInfinity);
        Action startAmountAct = () => tools.OrbitEntity(
            id: "iss",
            mode: "start",
            angularSpeedDegreesPerSecond: 12,
            amount: double.NaN);
        Action startHeadingAct = () => tools.OrbitEntity(
            id: "iss",
            mode: "start",
            angularSpeedDegreesPerSecond: 12,
            headingDegrees: double.NaN);

        stepAngularAct.Should().Throw<ArgumentOutOfRangeException>();
        stepHeadingAct.Should().Throw<ArgumentOutOfRangeException>();
        stepPitchAct.Should().Throw<ArgumentOutOfRangeException>();
        startAmountAct.Should().Throw<ArgumentOutOfRangeException>();
        startHeadingAct.Should().Throw<ArgumentOutOfRangeException>();
        collector.Drain().Should().BeEmpty();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("document")]
    public void OrbitEntity_RejectsBlankOrDocumentId_WithoutQueuingOperations(string id)
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action act = () => tools.OrbitEntity(id: id, mode: "step", amount: 45);

        act.Should().Throw<ArgumentException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void StopOrbit_QueuesSingleOrbitStopOperation()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        string result = tools.StopOrbit();

        result.Should().NotBeNullOrWhiteSpace();
        CameraSceneOp operation = collector.Drain().Should().ContainSingle().Which.Should().BeOfType<CameraSceneOp>().Subject;
        operation.Action.Should().Be(CameraAction.OrbitStop);
    }

    [Fact]
    public void UpdateEntityStyle_QueuesSingleStyleOperation_AfterValidation()
    {
        var collector = new SceneOpCollector();
        var validator = new RecordingStyleValidator();
        var tools = CreateTools(collector, validator);

        string result = tools.UpdateEntityStyle("iss", """{ "path": { "width": 5 } }""");

        result.Should().Contain("iss");
        validator.Calls.Should().ContainSingle();
        StyleSceneOp operation = collector.Drain().Should().ContainSingle().Which.Should().BeOfType<StyleSceneOp>().Subject;
        operation.Id.Should().Be("iss");
        operation.Patch.GetProperty("path").GetProperty("width").GetInt32().Should().Be(5);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("document")]
    public void UpdateEntityStyle_RejectsBlankOrDocumentId_WithoutQueuingOperations(string id)
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action act = () => tools.UpdateEntityStyle(id, """{ "path": { "width": 5 } }""");

        act.Should().Throw<ArgumentException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void UpdateEntityStyle_DoesNotQueueOperations_WhenPatchJsonIsInvalid()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Action act = () => tools.UpdateEntityStyle("iss", "{ not-json");

        act.Should().Throw<ArgumentException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void UpdateEntityStyle_DoesNotQueueOperations_WhenValidatorRejectsPatch()
    {
        var collector = new SceneOpCollector();
        var validator = new RecordingStyleValidator(_ => throw new ArgumentException("非法样式 patch"));
        var tools = CreateTools(collector, validator);

        Action act = () => tools.UpdateEntityStyle("iss", """{ "id": "hacked" }""");

        act.Should().Throw<ArgumentException>().WithMessage("非法样式 patch");
        collector.Drain().Should().BeEmpty();
        validator.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task PropagateAndAddSatellite_QueuesSingleUpsert_WithoutReturningLargePositions()
    {
        JsonElement expectedPacket = CreateSatellitePacket("sat-gen", "Generic Sat", "SGP4 hint");
        var orbitService = new StubOrbitScenarioService(
            createFromPropagation: (_, _, _, _, _, _, _, _) => Task.FromResult(expectedPacket));
        var collector = new SceneOpCollector();
        var tools = new SceneTools(collector, orbitService);

        string result = await tools.PropagateAndAddSatellite(
            id: "sat-gen",
            name: "Generic Sat",
            propagatorPath: "/Propagator/SGP4",
            requestJson: """{"Start":"2026-07-16T00:00:00Z","Stop":"2026-07-16T01:00:00Z","Step":60}""",
            startUtc: "2026-07-16T00:00:00Z",
            stopUtc: "2026-07-16T01:00:00Z",
            orbitHint: "SGP4 hint");

        result.Should().Contain("sat-gen");
        result.Should().NotContain("cartesian");
        result.Should().NotContain("1e6");
        orbitService.PropagationCalls.Should().ContainSingle();
        PropagationCall call = orbitService.PropagationCalls.Single();
        call.Id.Should().Be("sat-gen");
        call.Name.Should().Be("Generic Sat");
        call.PropagatorPath.Should().Be("/Propagator/SGP4");
        call.Request.GetProperty("Step").GetInt32().Should().Be(60);
        call.Request.GetProperty("Start").GetString().Should().Be("2026-07-16T00:00:00Z");
        call.Request.GetProperty("Stop").GetString().Should().Be("2026-07-16T01:00:00Z");
        call.StartUtc.Should().Be(DateTimeOffset.Parse("2026-07-16T00:00:00Z"));
        call.StopUtc.Should().Be(DateTimeOffset.Parse("2026-07-16T01:00:00Z"));
        call.OrbitHint.Should().Be("SGP4 hint");

        UpsertSceneOp operation = collector.Drain().Should().ContainSingle().Which.Should().BeOfType<UpsertSceneOp>().Subject;
        operation.Packets.Should().ContainSingle();
        operation.Packets.Single().GetRawText().Should().Be(expectedPacket.GetRawText());
    }

    [Fact]
    public async Task PropagateAndAddSatellite_DoesNotQueueOperations_WhenOrbitServiceFails()
    {
        var orbitService = new StubOrbitScenarioService(
            createFromPropagation: (_, _, _, _, _, _, _, _) =>
                throw new AstroxException("propagation unavailable"));
        var collector = new SceneOpCollector();
        var tools = new SceneTools(collector, orbitService);

        Func<Task> act = async () => await tools.PropagateAndAddSatellite(
            id: "sat-gen",
            name: null,
            propagatorPath: "/Propagator/TwoBody",
            requestJson: """{"Start":"2026-07-16T00:00:00Z","Stop":"2026-07-16T01:00:00Z","Step":60}""",
            startUtc: "2026-07-16T00:00:00Z",
            stopUtc: "2026-07-16T01:00:00Z");

        await act.Should().ThrowAsync<AstroxException>();
        collector.Drain().Should().BeEmpty();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("document")]
    public async Task PropagateAndAddSatellite_RejectsBlankOrDocumentId_WithoutQueuingOperations(string id)
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Func<Task> act = async () => await tools.PropagateAndAddSatellite(
            id: id,
            name: null,
            propagatorPath: "/Propagator/TwoBody",
            requestJson: """{"Start":"2026-07-16T00:00:00Z","Stop":"2026-07-16T01:00:00Z","Step":60}""",
            startUtc: "2026-07-16T00:00:00Z",
            stopUtc: "2026-07-16T01:00:00Z");

        await act.Should().ThrowAsync<ArgumentException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public async Task PropagateAndAddSatellite_RejectsInvalidRequestJsonOrWindow_WithoutQueuingOperations()
    {
        var collector = new SceneOpCollector();
        var tools = CreateTools(collector);

        Func<Task> invalidJson = async () => await tools.PropagateAndAddSatellite(
            "sat-1",
            null,
            "/Propagator/TwoBody",
            "{ not-json",
            "2026-07-16T00:00:00Z",
            "2026-07-16T01:00:00Z");
        Func<Task> invalidWindow = async () => await tools.PropagateAndAddSatellite(
            "sat-1",
            null,
            "/Propagator/TwoBody",
            """{"Start":"2026-07-16T00:00:00Z","Stop":"2026-07-17T00:00:01Z","Step":60}""",
            "2026-07-16T00:00:00Z",
            "2026-07-17T00:00:01Z");

        await invalidJson.Should().ThrowAsync<ArgumentException>();
        await invalidWindow.Should().ThrowAsync<ArgumentException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public async Task PropagateAndAddSatellite_RejectsRequestMissingStartStopStep_WithoutQueuingOperations()
    {
        // Stub 会接受非法请求；Tool/服务层必须在写 op 前拒绝。
        var orbitService = new StubOrbitScenarioService(
            createFromPropagation: (_, _, _, _, _, _, _, _) =>
                Task.FromResult(CreateSatellitePacket("sat-1", "sat-1", "x")));
        var collector = new SceneOpCollector();
        var tools = new SceneTools(collector, orbitService);

        Func<Task> act = async () => await tools.PropagateAndAddSatellite(
            "sat-1",
            null,
            "/Propagator/TwoBody",
            """{"Step":60}""",
            "2026-07-16T00:00:00Z",
            "2026-07-16T01:00:00Z");

        // 真实路径校验在 OrbitScenarioService；Tool 侧用真实服务测。
        // 此处用真实 OrbitScenarioService 的行为由 OrbitScenarioServiceTests 覆盖。
        // Tool 在调用 stub 前也应自行校验，避免绕过。
        await act.Should().ThrowAsync<ArgumentException>();
        collector.Drain().Should().BeEmpty();
        orbitService.PropagationCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task PropagateIssAndAddSatellite_InjectsStartStopStep_AndCallsSgp4Only()
    {
        JsonElement expectedPacket = CreateSatellitePacket("iss", "ISS", "ISS / SGP4");
        var orbitService = new StubOrbitScenarioService(
            createFromPropagation: (_, _, _, _, _, _, _, _) => Task.FromResult(expectedPacket));
        var collector = new SceneOpCollector();
        var now = DateTimeOffset.Parse("2026-07-16T10:08:59Z");
        var tools = new SceneTools(collector, orbitService, new FixedTimeProvider(now));

        // skill/TLE 构造的请求体：含 TLE 字段，故意带错误/缺失时间键；C# 只重写根 Start/Stop/Step。
        string requestJson = """
            {
              "Line1": "1 25544U 98067A   26197.5  .00000000  00000+0  00000+0 0  9990",
              "Line2": "2 25544  51.6400 123.4567 0001234  45.6789 314.5678 15.50000000123456",
              "start": "1999-01-01T00:00:00Z",
              "CentralBody": "Earth"
            }
            """;

        string result = await tools.PropagateIssAndAddSatellite(
            id: "iss",
            name: "ISS",
            requestJson: requestJson);

        result.Should().Contain("iss");
        orbitService.PropagationCalls.Should().ContainSingle();
        PropagationCall call = orbitService.PropagationCalls.Single();
        call.PropagatorPath.Should().Be("/Propagator/SGP4");
        call.StartUtc.Should().Be(DateTimeOffset.Parse("2026-07-16T10:08:00Z"));
        call.StopUtc.Should().Be(DateTimeOffset.Parse("2026-07-17T10:08:00Z"));
        call.OrbitHint.Should().Be("ISS / SGP4");

        JsonElement request = call.Request;
        request.GetProperty("Start").GetString().Should().Be("2026-07-16T10:08:00.000Z");
        request.GetProperty("Stop").GetString().Should().Be("2026-07-17T10:08:00.000Z");
        request.GetProperty("Step").GetInt32().Should().Be(60);
        // 不猜测/改写 TLE 字段
        request.GetProperty("Line1").GetString().Should().StartWith("1 25544");
        request.GetProperty("Line2").GetString().Should().StartWith("2 25544");
        request.GetProperty("CentralBody").GetString().Should().Be("Earth");
        // 错误大小写时间键被移除，仅保留精确 Start/Stop/Step
        request.TryGetProperty("start", out _).Should().BeFalse();

        collector.Drain().Should().ContainSingle().Which.Should().BeOfType<UpsertSceneOp>();
    }

    [Fact]
    public async Task PropagateIssAndAddSatellite_AllowsExplicitEpochHoursStepOverrides()
    {
        var orbitService = new StubOrbitScenarioService(
            createFromPropagation: (_, _, _, _, _, _, _, _) =>
                Task.FromResult(CreateSatellitePacket("iss", "ISS", "ISS / SGP4")));
        var collector = new SceneOpCollector();
        var tools = new SceneTools(
            collector,
            orbitService,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-07-16T10:08:59Z")));

        await tools.PropagateIssAndAddSatellite(
            id: "iss",
            name: null,
            requestJson: """{"Line1":"1 25544","Line2":"2 25544"}""",
            hours: 12,
            stepSeconds: 120,
            epochUtc: "2026-07-16T00:00:00Z");

        PropagationCall call = orbitService.PropagationCalls.Single();
        call.StartUtc.Should().Be(DateTimeOffset.Parse("2026-07-16T00:00:00Z"));
        call.StopUtc.Should().Be(DateTimeOffset.Parse("2026-07-16T12:00:00Z"));
        call.Request.GetProperty("Step").GetInt32().Should().Be(120);
        call.Name.Should().Be("iss");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24.1)]
    [InlineData(-1)]
    public async Task PropagateIssAndAddSatellite_RejectsInvalidHours_WithoutQueuing(double hours)
    {
        var orbitService = new StubOrbitScenarioService();
        var collector = new SceneOpCollector();
        var tools = new SceneTools(collector, orbitService);

        Func<Task> act = async () => await tools.PropagateIssAndAddSatellite(
            "iss",
            null,
            """{"Line1":"1","Line2":"2"}""",
            hours: hours);

        await act.Should().ThrowAsync<ArgumentException>();
        collector.Drain().Should().BeEmpty();
        orbitService.PropagationCalls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3601)]
    public async Task PropagateIssAndAddSatellite_RejectsInvalidStep_WithoutQueuing(int stepSeconds)
    {
        var orbitService = new StubOrbitScenarioService();
        var collector = new SceneOpCollector();
        var tools = new SceneTools(collector, orbitService);

        Func<Task> act = async () => await tools.PropagateIssAndAddSatellite(
            "iss",
            null,
            """{"Line1":"1","Line2":"2"}""",
            stepSeconds: stepSeconds);

        await act.Should().ThrowAsync<ArgumentException>();
        collector.Drain().Should().BeEmpty();
        orbitService.PropagationCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task PropagateIssAndAddSatellite_DoesNotQueue_WhenOrbitServiceFails()
    {
        var orbitService = new StubOrbitScenarioService(
            createFromPropagation: (_, _, _, _, _, _, _, _) =>
                throw new AstroxException("sgp4 failed"));
        var collector = new SceneOpCollector();
        var tools = new SceneTools(collector, orbitService);

        Func<Task> act = async () => await tools.PropagateIssAndAddSatellite(
            "iss",
            "ISS",
            """{"Line1":"1 25544","Line2":"2 25544"}""");

        await act.Should().ThrowAsync<AstroxException>();
        collector.Drain().Should().BeEmpty();
    }

    [Fact]
    public void AddSatelliteFromPositions_QueuesSingleUpsert_WithoutEchoingLargePositions()
    {
        JsonElement expectedPacket = CreateSatellitePacket("sat-pos", "sat-pos", "external");
        var orbitService = new StubOrbitScenarioService(
            createFromPositions: (_, _, _, _, _, _) => expectedPacket);
        var collector = new SceneOpCollector();
        var tools = new SceneTools(collector, orbitService);
        string largePositionJson = """
            {
              "epoch": "2026-07-16T00:00:00.000Z",
              "cartesian": [0, 1000000, 2000000, 3000000, 60, 4000000, 5000000, 6000000]
            }
            """;

        string result = tools.AddSatelliteFromPositions(
            id: "sat-pos",
            name: null,
            positionJson: largePositionJson,
            startUtc: "2026-07-16T00:00:00Z",
            stopUtc: "2026-07-16T01:00:00Z",
            orbitHint: "external");

        result.Should().Contain("sat-pos");
        result.Should().NotContain("1000000");
        result.Should().NotContain("cartesian");
        orbitService.PositionCalls.Should().ContainSingle();
        UpsertSceneOp operation = collector.Drain().Should().ContainSingle().Which.Should().BeOfType<UpsertSceneOp>().Subject;
        operation.Packets.Single().GetRawText().Should().Be(expectedPacket.GetRawText());
    }

    [Fact]
    public void AddSatelliteFromPositions_DoesNotQueueOperations_WhenValidationFails()
    {
        var orbitService = new StubOrbitScenarioService(
            createFromPositions: (_, _, _, _, _, _) =>
                throw new ArgumentException("invalid position"));
        var collector = new SceneOpCollector();
        var tools = new SceneTools(collector, orbitService);

        Action act = () => tools.AddSatelliteFromPositions(
            id: "sat-pos",
            name: null,
            positionJson: """{"epoch":"2026-07-16T00:00:00.000Z","cartesian":[0,1,2,3]}""",
            startUtc: "2026-07-16T00:00:00Z",
            stopUtc: "2026-07-16T01:00:00Z");

        act.Should().Throw<ArgumentException>();
        collector.Drain().Should().BeEmpty();
    }

    private static JsonElement CreateSatellitePacket(string id, string name, string orbitHint)
        => JsonSerializer.SerializeToElement(new
        {
            id,
            name,
            availability = "2026-07-16T00:00:00.000Z/2026-07-16T01:00:00.000Z",
            position = new
            {
                epoch = "2026-07-16T00:00:00.000Z",
                cartesian = new[] { 0, 1, 2, 3 }
            },
            point = new
            {
                pixelSize = 8,
                color = new { rgba = new[] { 255, 220, 0, 255 } }
            },
            path = new
            {
                show = true,
                width = 2,
                leadTime = 0,
                trailTime = 3600
            },
            properties = new
            {
                orbitHint = new { @string = orbitHint }
            }
        });

    private static SceneTools CreateTools(
        ISceneOpSink sink,
        ISceneStyleValidator? styleValidator = null)
        => new(sink, new StubOrbitScenarioService(), styleValidator: styleValidator ?? new SceneStyleValidator());

    private sealed class RecordingStyleValidator(
        Func<JsonElement, JsonElement>? validate = null) : ISceneStyleValidator
    {
        private readonly Func<JsonElement, JsonElement> _validate =
            validate ?? (patch => patch.Clone());

        public List<JsonElement> Calls { get; } = [];

        public JsonElement ValidateAndClone(JsonElement patch)
        {
            Calls.Add(patch.Clone());
            return _validate(patch);
        }
    }

    private sealed record PropagationCall(
        string Id,
        string Name,
        string PropagatorPath,
        JsonElement Request,
        DateTimeOffset StartUtc,
        DateTimeOffset StopUtc,
        string? OrbitHint);

    private sealed record PositionCall(
        string Id,
        string Name,
        JsonElement Position,
        DateTimeOffset StartUtc,
        DateTimeOffset StopUtc,
        string? OrbitHint);

    private sealed class StubOrbitScenarioService(
        Func<SsoJ2Scenario, CancellationToken, Task<JsonElement>>? createPacket = null,
        Func<string, string, string, JsonElement, DateTimeOffset, DateTimeOffset, string?, CancellationToken, Task<JsonElement>>? createFromPropagation = null,
        Func<string, string, JsonElement, DateTimeOffset, DateTimeOffset, string?, JsonElement>? createFromPositions = null) : IOrbitScenarioService
    {
        private readonly Func<SsoJ2Scenario, CancellationToken, Task<JsonElement>> _createPacket =
            createPacket ?? ((_, _) => Task.FromResult(JsonSerializer.SerializeToElement(new { id = "default-sat" })));

        private readonly Func<string, string, string, JsonElement, DateTimeOffset, DateTimeOffset, string?, CancellationToken, Task<JsonElement>> _createFromPropagation =
            createFromPropagation
            ?? ((_, _, _, _, _, _, _, _) => Task.FromResult(JsonSerializer.SerializeToElement(new { id = "default-sat" })));

        private readonly Func<string, string, JsonElement, DateTimeOffset, DateTimeOffset, string?, JsonElement> _createFromPositions =
            createFromPositions
            ?? ((_, _, _, _, _, _) => JsonSerializer.SerializeToElement(new { id = "default-sat" }));

        public List<SsoJ2Scenario> Scenarios { get; } = [];

        public List<PropagationCall> PropagationCalls { get; } = [];

        public List<PositionCall> PositionCalls { get; } = [];

        public Task<JsonElement> CreateSsoJ2PacketAsync(SsoJ2Scenario scenario, CancellationToken cancellationToken)
        {
            Scenarios.Add(scenario);
            return _createPacket(scenario, cancellationToken);
        }

        public Task<JsonElement> CreatePacketFromPropagationAsync(
            string id,
            string name,
            string propagatorPath,
            JsonElement request,
            DateTimeOffset startUtc,
            DateTimeOffset stopUtc,
            string? orbitHint,
            CancellationToken cancellationToken)
        {
            PropagationCalls.Add(new PropagationCall(
                id,
                name,
                propagatorPath,
                request.Clone(),
                startUtc,
                stopUtc,
                orbitHint));
            return _createFromPropagation(
                id,
                name,
                propagatorPath,
                request,
                startUtc,
                stopUtc,
                orbitHint,
                cancellationToken);
        }

        public JsonElement CreatePacketFromPositions(
            string id,
            string name,
            JsonElement position,
            DateTimeOffset startUtc,
            DateTimeOffset stopUtc,
            string? orbitHint)
        {
            PositionCalls.Add(new PositionCall(
                id,
                name,
                position.Clone(),
                startUtc,
                stopUtc,
                orbitHint));
            return _createFromPositions(id, name, position, startUtc, stopUtc, orbitHint);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
