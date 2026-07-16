using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using CesiumAI.Api.Astrox;
using CesiumAI.Api.Models;
using CesiumAI.Api.Services;

namespace CesiumAI.Api.Tools;

public sealed class SceneTools(
    ISceneOpSink sceneOpSink,
    IOrbitScenarioService orbitScenarioService,
    TimeProvider? timeProvider = null,
    ISceneStyleValidator? styleValidator = null)
{
    private static readonly HashSet<string> AllowedPanDirections = new(StringComparer.Ordinal)
    {
        "left",
        "right",
        "up",
        "down"
    };

    private readonly ISceneOpSink _sceneOpSink = sceneOpSink ?? throw new ArgumentNullException(nameof(sceneOpSink));
    private readonly IOrbitScenarioService _orbitScenarioService = orbitScenarioService ?? throw new ArgumentNullException(nameof(orbitScenarioService));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ISceneStyleValidator _styleValidator = styleValidator ?? new SceneStyleValidator();

    [Description("Queue a clear-scene operation.")]
    public string ClearScene()
    {
        _sceneOpSink.Add(new ClearSceneOp());
        return "Scene clear queued.";
    }

    [Description("Queue a facility packet upsert.")]
    public string UpsertFacility(
        string id,
        string? name,
        double longitudeDegrees,
        double latitudeDegrees,
        double altitudeMeters = 0)
    {
        string facilityId = ValidateRequiredId(id, nameof(id));
        ValidateLongitude(longitudeDegrees);
        ValidateLatitude(latitudeDegrees);

        string facilityName = string.IsNullOrWhiteSpace(name) ? facilityId : name;

        JsonElement packet = JsonSerializer.SerializeToElement(new
        {
            id = facilityId,
            name = facilityName,
            position = new
            {
                cartographicDegrees = new[] { longitudeDegrees, latitudeDegrees, altitudeMeters }
            },
            point = new
            {
                pixelSize = 10,
                color = new
                {
                    rgba = new[] { 255, 80, 80, 255 }
                },
                outlineColor = new
                {
                    rgba = new[] { 255, 255, 255, 255 }
                },
                outlineWidth = 2
            },
            label = new
            {
                text = facilityName,
                show = true,
                pixelOffset = new
                {
                    cartesian2 = new[] { 0, -18 }
                }
            }
        });

        _sceneOpSink.Add(new UpsertSceneOp([packet]));
        return $"Facility '{facilityId}' queued for upsert.";
    }

    [Description("Queue entity deletions by id.")]
    public string DeleteEntity(string[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        string[] filteredIds = ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(id => !string.Equals(id, "document", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (filteredIds.Length == 0)
        {
            return "No entity ids queued for deletion.";
        }

        _sceneOpSink.Add(new DeleteSceneOp(filteredIds));

        return filteredIds.Length == 1
            ? $"Entity '{filteredIds[0]}' queued for deletion."
            : $"{filteredIds.Length} entities queued for deletion.";
    }

    [Description("Queue a sun-synchronous J2 satellite packet upsert.")]
    public async Task<string> AddSatelliteJ2(
        string id,
        string? name = null,
        double altitudeKm = 900,
        double hours = 24,
        int stepSeconds = 60,
        double localTimeOfDescendingNode = 10.5,
        string? epochUtc = null,
        CancellationToken cancellationToken = default)
    {
        string satelliteId = ValidateRequiredId(id, nameof(id));
        string satelliteName = string.IsNullOrWhiteSpace(name) ? satelliteId : name;
        DateTimeOffset epoch = epochUtc is null
            ? TruncateToMinute(_timeProvider.GetUtcNow())
            : ParseEpochUtc(epochUtc);

        JsonElement packet = await _orbitScenarioService.CreateSsoJ2PacketAsync(
            new SsoJ2Scenario(
                Id: satelliteId,
                Name: satelliteName,
                AltitudeKm: altitudeKm,
                EpochUtc: epoch,
                Hours: hours,
                StepSeconds: stepSeconds,
                LocalTimeOfDescendingNode: localTimeOfDescendingNode),
            cancellationToken);

        _sceneOpSink.Add(new UpsertSceneOp([packet.Clone()]));
        return $"Satellite '{satelliteId}' queued for upsert.";
    }

    [Description("Fly the camera to focus on an entity. Distances use meters; angles use degrees.")]
    public string FocusEntity(
        string id,
        double? distanceMeters = null,
        double? headingDegrees = null,
        double? pitchDegrees = null)
    {
        string targetId = ValidateEntityId(id, nameof(id));
        double? validatedDistance = ValidateOptionalPositiveDistance(distanceMeters);
        EnsureOptionalFinite(headingDegrees, nameof(headingDegrees));
        EnsureOptionalFinite(pitchDegrees, nameof(pitchDegrees));

        _sceneOpSink.Add(new CameraSceneOp(
            CameraAction.Focus,
            TargetId: targetId,
            DistanceMeters: validatedDistance,
            HeadingDegrees: headingDegrees,
            PitchDegrees: pitchDegrees));

        return $"Camera focus on '{targetId}' queued.";
    }

    [Description("Start tracking an entity with the Cesium trackedEntity camera.")]
    public string TrackEntity(string id)
    {
        string targetId = ValidateEntityId(id, nameof(id));
        _sceneOpSink.Add(new CameraSceneOp(CameraAction.Track, TargetId: targetId));
        return $"Camera tracking '{targetId}' queued.";
    }

    [Description("Stop tracking the current entity.")]
    public string StopTracking()
    {
        _sceneOpSink.Add(new CameraSceneOp(CameraAction.Untrack));
        return "Camera untrack queued.";
    }

    [Description("Adjust the camera relatively. action is zoom|pan|rotate. Distances use meters; angles use degrees. rotate 的 headingDegrees：正为右转，负为左转。")]
    public string AdjustCamera(
        string action,
        double? amount = null,
        string? direction = null,
        double? headingDegrees = null,
        double? pitchDegrees = null,
        double? rollDegrees = null)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("Camera action cannot be blank.", nameof(action));
        }

        string normalizedAction = action.Trim();
        CameraAction cameraAction = normalizedAction.ToLowerInvariant() switch
        {
            "zoom" => CameraAction.Zoom,
            "pan" => CameraAction.Pan,
            "rotate" => CameraAction.Rotate,
            _ => throw new ArgumentException("Camera action must be zoom, pan, or rotate.", nameof(action))
        };

        // 所有数值参数均拒绝 NaN/Infinity，避免不适用字段把非有限值写入 op。
        EnsureOptionalFinite(amount, nameof(amount));
        EnsureOptionalFinite(headingDegrees, nameof(headingDegrees));
        EnsureOptionalFinite(pitchDegrees, nameof(pitchDegrees));
        EnsureOptionalFinite(rollDegrees, nameof(rollDegrees));

        string? validatedDirection = null;

        switch (cameraAction)
        {
            case CameraAction.Zoom:
                if (amount is null || amount == 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(amount), amount, "Zoom amount cannot be zero and must be finite.");
                }

                break;

            case CameraAction.Pan:
                validatedDirection = ValidatePanDirection(direction);
                if (amount is null || amount <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(amount),
                        amount,
                        "Pan amount must be a positive finite distance in meters.");
                }

                break;

            case CameraAction.Rotate:
                break;
        }

        _sceneOpSink.Add(new CameraSceneOp(
            cameraAction,
            Amount: amount,
            Direction: validatedDirection,
            HeadingDegrees: headingDegrees,
            PitchDegrees: pitchDegrees,
            RollDegrees: rollDegrees));

        return $"Camera {normalizedAction.ToLowerInvariant()} adjustment queued.";
    }

    [Description("Orbit around an entity. mode is step|start. Angles and angular speed use degrees.")]
    public string OrbitEntity(
        string id,
        string mode,
        double? amount = null,
        double? angularSpeedDegreesPerSecond = null,
        double? headingDegrees = null,
        double? pitchDegrees = null,
        double? distanceMeters = null)
    {
        string targetId = ValidateEntityId(id, nameof(id));
        if (string.IsNullOrWhiteSpace(mode))
        {
            throw new ArgumentException("Orbit mode cannot be blank.", nameof(mode));
        }

        string normalizedMode = mode.Trim();
        // 模式字面量大小写敏感，仅接受 step|start。
        CameraAction cameraAction = normalizedMode switch
        {
            "step" => CameraAction.OrbitStep,
            "start" => CameraAction.OrbitStart,
            _ => throw new ArgumentException("Orbit mode must be step or start.", nameof(mode))
        };

        // 所有数值参数均拒绝 NaN/Infinity，避免不适用字段把非有限值写入 op。
        EnsureOptionalFinite(amount, nameof(amount));
        EnsureOptionalFinite(angularSpeedDegreesPerSecond, nameof(angularSpeedDegreesPerSecond));
        EnsureOptionalFinite(headingDegrees, nameof(headingDegrees));
        EnsureOptionalFinite(pitchDegrees, nameof(pitchDegrees));
        double? validatedDistance = ValidateOptionalPositiveDistance(distanceMeters);

        if (cameraAction == CameraAction.OrbitStep)
        {
            if (amount is null || amount == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount),
                    amount,
                    "Orbit step amount must be a non-zero finite angle in degrees.");
            }
        }
        else
        {
            if (angularSpeedDegreesPerSecond is null || angularSpeedDegreesPerSecond.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(angularSpeedDegreesPerSecond),
                    angularSpeedDegreesPerSecond,
                    "Angular speed must be greater than 0.");
            }
        }

        _sceneOpSink.Add(new CameraSceneOp(
            cameraAction,
            TargetId: targetId,
            Amount: amount,
            AngularSpeedDegreesPerSecond: angularSpeedDegreesPerSecond,
            HeadingDegrees: headingDegrees,
            PitchDegrees: pitchDegrees,
            DistanceMeters: validatedDistance));

        return cameraAction == CameraAction.OrbitStep
            ? $"Camera orbit step around '{targetId}' queued."
            : $"Camera orbit start around '{targetId}' queued.";
    }

    [Description("Stop continuous camera orbiting.")]
    public string StopOrbit()
    {
        _sceneOpSink.Add(new CameraSceneOp(CameraAction.OrbitStop));
        return "Camera orbit stop queued.";
    }

    [Description("Propagate an orbit via an Astrox /Propagator/* endpoint and queue a satellite packet upsert. Large positions stay server-side and are not returned to the model.")]
    public async Task<string> PropagateAndAddSatellite(
        string id,
        string? name,
        string propagatorPath,
        string requestJson,
        string startUtc,
        string stopUtc,
        string? orbitHint = null,
        CancellationToken cancellationToken = default)
    {
        string satelliteId = ValidateEntityId(id, nameof(id));
        string satelliteName = string.IsNullOrWhiteSpace(name) ? satelliteId : name.Trim();
        DateTimeOffset start = ParseRequiredUtc(startUtc, nameof(startUtc));
        DateTimeOffset stop = ParseRequiredUtc(stopUtc, nameof(stopUtc));
        ValidateAvailabilityWindow(start, stop);

        if (string.IsNullOrWhiteSpace(propagatorPath))
        {
            throw new ArgumentException("Propagator path cannot be blank.", nameof(propagatorPath));
        }

        if (string.IsNullOrWhiteSpace(requestJson))
        {
            throw new ArgumentException("Request JSON cannot be blank.", nameof(requestJson));
        }

        JsonElement request;
        try
        {
            using JsonDocument document = JsonDocument.Parse(requestJson);
            request = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Request JSON is invalid.", nameof(requestJson), ex);
        }

        JsonElement packet = await _orbitScenarioService.CreatePacketFromPropagationAsync(
            satelliteId,
            satelliteName,
            propagatorPath.Trim(),
            request,
            start,
            stop,
            orbitHint,
            cancellationToken);

        _sceneOpSink.Add(new UpsertSceneOp([packet.Clone()]));
        // 不向模型回传大型 Position，仅返回简短确认。
        return $"Satellite '{satelliteId}' queued for upsert.";
    }

    [Description("Queue a satellite packet upsert from a validated CZML position JSON. Large positions are not echoed back to the model.")]
    public string AddSatelliteFromPositions(
        string id,
        string? name,
        string positionJson,
        string startUtc,
        string stopUtc,
        string? orbitHint = null)
    {
        string satelliteId = ValidateEntityId(id, nameof(id));
        string satelliteName = string.IsNullOrWhiteSpace(name) ? satelliteId : name.Trim();
        DateTimeOffset start = ParseRequiredUtc(startUtc, nameof(startUtc));
        DateTimeOffset stop = ParseRequiredUtc(stopUtc, nameof(stopUtc));
        ValidateAvailabilityWindow(start, stop);

        if (string.IsNullOrWhiteSpace(positionJson))
        {
            throw new ArgumentException("Position JSON cannot be blank.", nameof(positionJson));
        }

        JsonElement position;
        try
        {
            using JsonDocument document = JsonDocument.Parse(positionJson);
            position = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Position JSON is invalid.", nameof(positionJson), ex);
        }

        JsonElement packet = _orbitScenarioService.CreatePacketFromPositions(
            satelliteId,
            satelliteName,
            position,
            start,
            stop,
            orbitHint);

        _sceneOpSink.Add(new UpsertSceneOp([packet.Clone()]));
        // 不向模型回传大型 Position，仅返回简短确认。
        return $"Satellite '{satelliteId}' queued for upsert.";
    }

    [Description("Update allowed visual style properties of an existing entity via a JSON patch.")]
    public string UpdateEntityStyle(string id, string patchJson)
    {
        string entityId = ValidateEntityId(id, nameof(id));
        if (string.IsNullOrWhiteSpace(patchJson))
        {
            throw new ArgumentException("Style patch JSON cannot be blank.", nameof(patchJson));
        }

        JsonElement parsed;
        try
        {
            using JsonDocument document = JsonDocument.Parse(patchJson);
            parsed = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Style patch JSON is invalid.", nameof(patchJson), ex);
        }

        JsonElement validatedPatch = _styleValidator.ValidateAndClone(parsed);
        _sceneOpSink.Add(new StyleSceneOp(entityId, validatedPatch));
        return $"Style update for '{entityId}' queued.";
    }

    private static string ValidateRequiredId(string id, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Value cannot be blank.", parameterName);
        }

        return id.Trim();
    }

    private static string ValidateEntityId(string id, string parameterName)
    {
        string trimmed = ValidateRequiredId(id, parameterName);
        if (string.Equals(trimmed, "document", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Document id cannot be used as an entity target.", parameterName);
        }

        return trimmed;
    }

    private static double? ValidateOptionalPositiveDistance(double? distanceMeters)
    {
        if (distanceMeters is null)
        {
            return null;
        }

        if (!double.IsFinite(distanceMeters.Value) || distanceMeters.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(distanceMeters),
                distanceMeters,
                "Distance must be greater than 0 meters.");
        }

        return distanceMeters;
    }

    private static string ValidatePanDirection(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction))
        {
            throw new ArgumentException("Pan direction is required.", nameof(direction));
        }

        string normalized = direction.Trim();
        if (!AllowedPanDirections.Contains(normalized))
        {
            throw new ArgumentException("Pan direction must be left, right, up, or down.", nameof(direction));
        }

        return normalized;
    }

    private static void EnsureOptionalFinite(double? value, string parameterName)
    {
        if (value is not null && !double.IsFinite(value.Value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be a finite number.");
        }
    }

    private static void ValidateLongitude(double longitudeDegrees)
    {
        if (!double.IsFinite(longitudeDegrees) || longitudeDegrees < -180 || longitudeDegrees > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitudeDegrees), longitudeDegrees, "Longitude must be between -180 and 180 degrees.");
        }
    }

    private static void ValidateLatitude(double latitudeDegrees)
    {
        if (!double.IsFinite(latitudeDegrees) || latitudeDegrees < -90 || latitudeDegrees > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitudeDegrees), latitudeDegrees, "Latitude must be between -90 and 90 degrees.");
        }
    }

    private static DateTimeOffset ParseEpochUtc(string epochUtc)
    {
        if (string.IsNullOrWhiteSpace(epochUtc))
        {
            throw new ArgumentException("Epoch UTC cannot be blank when provided.", nameof(epochUtc));
        }

        if (!DateTimeOffset.TryParse(
                epochUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed))
        {
            throw new ArgumentException("Epoch UTC must be a valid timestamp.", nameof(epochUtc));
        }

        return parsed;
    }

    private static DateTimeOffset ParseRequiredUtc(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("UTC timestamp cannot be blank.", parameterName);
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed))
        {
            throw new ArgumentException("UTC timestamp must be valid.", parameterName);
        }

        return parsed;
    }

    private static void ValidateAvailabilityWindow(DateTimeOffset startUtc, DateTimeOffset stopUtc)
    {
        if (stopUtc <= startUtc)
        {
            throw new ArgumentException("Availability stop 必须晚于 start。");
        }

        if (stopUtc - startUtc > TimeSpan.FromHours(24))
        {
            throw new ArgumentException("Availability 窗口不能超过 24 小时。");
        }
    }

    private static DateTimeOffset TruncateToMinute(DateTimeOffset value)
    {
        DateTimeOffset utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
    }
}
