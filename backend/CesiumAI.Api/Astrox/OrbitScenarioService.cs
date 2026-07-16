using System.Globalization;
using System.Text.Json;

namespace CesiumAI.Api.Astrox;

public sealed class OrbitScenarioService(IAstroxClient astroxClient) : IOrbitScenarioService
{
    private readonly IAstroxClient _astroxClient = astroxClient ?? throw new ArgumentNullException(nameof(astroxClient));

    public async Task<JsonElement?> CreateSsoJ2PacketAsync(SsoJ2Scenario scenario, CancellationToken cancellationToken)
    {
        ValidateScenario(scenario);

        try
        {
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

            int trailTimeSeconds = checked((int)TimeSpan.FromHours(scenario.Hours).TotalSeconds);

            return JsonSerializer.SerializeToElement(new
            {
                id = scenario.Id,
                name = scenario.Name,
                availability = $"{FormatUtc(scenario.EpochUtc)}/{FormatUtc(stop)}",
                position = j2Response.Position,
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
                properties = new
                {
                    orbitHint = new
                    {
                        @string = $"{FormatAltitude(scenario.AltitudeKm)} km SSO / J2"
                    }
                }
            });
        }
        catch (AstroxException)
        {
            return null;
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
