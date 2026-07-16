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
    TimeProvider? timeProvider = null)
{
    private readonly ISceneOpSink _sceneOpSink = sceneOpSink ?? throw new ArgumentNullException(nameof(sceneOpSink));
    private readonly IOrbitScenarioService _orbitScenarioService = orbitScenarioService ?? throw new ArgumentNullException(nameof(orbitScenarioService));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

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

    private static string ValidateRequiredId(string id, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Value cannot be blank.", parameterName);
        }

        return id.Trim();
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

    private static DateTimeOffset TruncateToMinute(DateTimeOffset value)
    {
        DateTimeOffset utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
    }
}
