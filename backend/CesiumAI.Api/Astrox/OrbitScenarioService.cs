using System.Globalization;
using System.Text.Json;

namespace CesiumAI.Api.Astrox;

public sealed class OrbitScenarioService : IOrbitScenarioService
{
    private readonly IAstroxClient _astroxClient;
    private readonly ICzmlPositionValidator _positionValidator;

    public OrbitScenarioService(
        IAstroxClient astroxClient,
        ICzmlPositionValidator? positionValidator = null)
    {
        _astroxClient = astroxClient ?? throw new ArgumentNullException(nameof(astroxClient));
        _positionValidator = positionValidator ?? new CzmlPositionValidator();
    }

    public async Task<JsonElement> CreateSsoJ2PacketAsync(SsoJ2Scenario scenario, CancellationToken cancellationToken)
    {
        ValidateScenario(scenario);

        SsoResponse ssoResponse = await _astroxClient.CreateSsoAsync(
            new SsoRequest(
                Description: $"SSO-{FormatAltitude(scenario.AltitudeKm)}",
                OrbitEpoch: scenario.EpochUtc,
                Altitude: scenario.AltitudeKm,
                LocalTimeOfDescendingNode: scenario.LocalTimeOfDescendingNode),
            cancellationToken);

        DateTimeOffset stop = scenario.EpochUtc.AddHours(scenario.Hours);
        J2Response j2Response = await _astroxClient.PropagateJ2Async(
            new J2Request(
                Start: scenario.EpochUtc,
                Stop: stop,
                CentralBody: "Earth",
                OrbitEpoch: scenario.EpochUtc,
                CoordType: "Classical",
                OrbitalElements:
                [
                    ssoResponse.ElementsInertial.SemimajorAxis,
                    ssoResponse.ElementsInertial.Eccentricity,
                    ssoResponse.ElementsInertial.Inclination,
                    ssoResponse.ElementsInertial.ArgumentOfPeriapsis,
                    ssoResponse.ElementsInertial.RightAscensionOfAscendingNode,
                    ssoResponse.ElementsInertial.TrueAnomaly
                ],
                Step: scenario.StepSeconds),
            cancellationToken);

        return BuildSatellitePacket(
            scenario.Id,
            scenario.Name,
            scenario.EpochUtc,
            stop,
            j2Response.Position,
            $"{FormatAltitude(scenario.AltitudeKm)} km SSO / J2");
    }

    public async Task<JsonElement> CreatePacketFromPropagationAsync(
        string id,
        string name,
        string propagatorPath,
        JsonElement request,
        DateTimeOffset startUtc,
        DateTimeOffset stopUtc,
        string? orbitHint,
        CancellationToken cancellationToken)
    {
        ValidatePacketIdentity(id, name);
        ValidateAvailabilityWindow(startUtc, stopUtc);
        // 发往 Astrox 前强制校验请求根 Start/Stop/Step，失败不发 HTTP。
        PropagationRequestValidator.Validate(request, startUtc, stopUtc);

        GenericPropagationResponse response = await _astroxClient.PropagateAsync(
            propagatorPath,
            request,
            cancellationToken);

        JsonElement validatedPosition = _positionValidator.ValidateAndClone(
            response.Position,
            startUtc,
            stopUtc);

        return BuildSatellitePacket(
            id,
            name,
            startUtc,
            stopUtc,
            validatedPosition,
            orbitHint);
    }

    public JsonElement CreatePacketFromPositions(
        string id,
        string name,
        JsonElement position,
        DateTimeOffset startUtc,
        DateTimeOffset stopUtc,
        string? orbitHint)
    {
        ValidatePacketIdentity(id, name);
        ValidateAvailabilityWindow(startUtc, stopUtc);

        JsonElement validatedPosition = _positionValidator.ValidateAndClone(
            position,
            startUtc,
            stopUtc);

        return BuildSatellitePacket(
            id,
            name,
            startUtc,
            stopUtc,
            validatedPosition,
            orbitHint);
    }

    private static JsonElement BuildSatellitePacket(
        string id,
        string name,
        DateTimeOffset startUtc,
        DateTimeOffset stopUtc,
        JsonElement position,
        string? orbitHint)
    {
        int trailTimeSeconds = checked((int)(stopUtc - startUtc).TotalSeconds);
        string hint = string.IsNullOrWhiteSpace(orbitHint) ? name : orbitHint.Trim();

        return JsonSerializer.SerializeToElement(new
        {
            id,
            name,
            availability = $"{FormatUtc(startUtc)}/{FormatUtc(stopUtc)}",
            position,
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
                trailTime = trailTimeSeconds,
                material = new
                {
                    solidColor = new
                    {
                        color = new
                        {
                            rgba = new[] { 0, 200, 255, 220 }
                        }
                    }
                }
            },
            model = new
            {
                gltf = "/models/satellite.glb",
                minimumPixelSize = 64,
                maximumScale = 20000
            },
            properties = new
            {
                orbitHint = new
                {
                    @string = hint
                }
            }
        });
    }

    private static void ValidatePacketIdentity(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Satellite id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Satellite name is required.", nameof(name));
        }
    }

    private static void ValidateAvailabilityWindow(DateTimeOffset startUtc, DateTimeOffset stopUtc)
    {
        DateTimeOffset start = startUtc.ToUniversalTime();
        DateTimeOffset stop = stopUtc.ToUniversalTime();

        if (stop <= start)
        {
            throw new ArgumentException("Availability stop 必须晚于 start。");
        }

        if (stop - start > TimeSpan.FromHours(24))
        {
            throw new ArgumentException("Availability 窗口不能超过 24 小时。");
        }
    }

    private static void ValidateScenario(SsoJ2Scenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        if (string.IsNullOrWhiteSpace(scenario.Id))
        {
            throw new ArgumentException("Scenario id is required.", nameof(scenario));
        }

        if (string.IsNullOrWhiteSpace(scenario.Name))
        {
            throw new ArgumentException("Scenario name is required.", nameof(scenario));
        }

        if (scenario.AltitudeKm is < 100 or > 100000)
        {
            throw new ArgumentOutOfRangeException(nameof(scenario), scenario.AltitudeKm, "Altitude must be between 100 km and 100000 km.");
        }

        if (scenario.Hours <= 0 || scenario.Hours > 24)
        {
            throw new ArgumentOutOfRangeException(nameof(scenario), scenario.Hours, "Hours must be greater than 0 and at most 24.");
        }

        if (scenario.StepSeconds is < 1 or > 3600)
        {
            throw new ArgumentOutOfRangeException(nameof(scenario), scenario.StepSeconds, "Step seconds must be between 1 and 3600.");
        }

        if (scenario.LocalTimeOfDescendingNode < 0 || scenario.LocalTimeOfDescendingNode >= 24)
        {
            throw new ArgumentOutOfRangeException(nameof(scenario), scenario.LocalTimeOfDescendingNode, "Local descending-node time must be between 0 inclusive and 24 exclusive.");
        }
    }

    private static string FormatUtc(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string FormatAltitude(double altitudeKm)
        => altitudeKm.ToString("0.###", CultureInfo.InvariantCulture);
}
