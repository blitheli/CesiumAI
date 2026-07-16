using System.Net;
using System.Text.Json;
using CesiumAI.Api.Astrox;
using CesiumAI.Api.Configuration;
using CesiumAI.Api.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CesiumAI.Api.Tests.Astrox;

public class OrbitScenarioServiceTests
{
    [Fact]
    public async Task CreateSsoJ2PacketAsync_CallsSsoBeforeJ2_UsesAstroxElementOrder_AndBuildsCompleteSatellitePacket()
    {
        var requestedPaths = new List<string>();
        string? capturedJ2Body = null;

        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            requestedPaths.Add(request.RequestUri!.AbsolutePath);

            if (request.RequestUri.AbsolutePath == "/OrbitWizard/SSO")
            {
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, """
                    {
                      "IsSuccess": true,
                      "Message": "ok",
                      "Elements_Inertial": {
                        "SemimajorAxis": 7278136.3,
                        "Eccentricity": 0.001,
                        "Inclination": 98.9,
                        "ArgumentOfPeriapsis": 0.2,
                        "RightAscensionOfAscendingNode": 15.4,
                        "TrueAnomaly": 22.8,
                        "GravitationalParameter": 398600441800000
                      }
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath == "/Propagator/J2")
            {
                capturedJ2Body = await request.Content!.ReadAsStringAsync();

                return StubHttpMessageHandler.Json(HttpStatusCode.OK, """
                    {
                      "IsSuccess": true,
                      "Message": "ok",
                      "Position": {
                        "cartesianVelocity": {
                          "epoch": "2026-07-16T00:00:00.000Z",
                          "cartesian": [0, 1, 2, 3, 4, 5]
                        }
                      },
                      "Period": 6000
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected path {request.RequestUri.AbsolutePath}");
        });

        var client = CreateAstroxClient(handler);
        var service = new OrbitScenarioService(client);

        JsonElement? packet = await service.CreateSsoJ2PacketAsync(CreateScenario(), CancellationToken.None);

        requestedPaths.Should().Equal("/OrbitWizard/SSO", "/Propagator/J2");
        capturedJ2Body.Should().NotBeNull();

        using JsonDocument j2RequestDocument = JsonDocument.Parse(capturedJ2Body!);
        j2RequestDocument.RootElement.GetProperty("OrbitalElements")
            .EnumerateArray()
            .Select(value => value.GetDouble())
            .Should()
            .Equal([7278136.3, 0.001, 98.9, 0.2, 15.4, 22.8]);

        packet.Should().NotBeNull();
        JsonElement root = packet!.Value;

        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["id", "name", "availability", "position", "point", "path", "properties"]);
        root.GetProperty("id").GetString().Should().Be("sso-900");
        root.GetProperty("name").GetString().Should().Be("SSO 900 km");
        root.GetProperty("availability").GetString().Should().Be("2026-07-16T00:00:00.000Z/2026-07-17T00:00:00.000Z");

        JsonElement position = root.GetProperty("position");
        position.GetProperty("cartesianVelocity").GetProperty("epoch").GetString().Should().Be("2026-07-16T00:00:00.000Z");
        position.GetProperty("cartesianVelocity").GetProperty("cartesian")
            .EnumerateArray()
            .Select(value => value.GetDouble())
            .Should()
            .Equal([0, 1, 2, 3, 4, 5]);

        JsonElement point = root.GetProperty("point");
        point.GetProperty("pixelSize").GetInt32().Should().Be(8);
        point.GetProperty("color").GetProperty("rgba").EnumerateArray().Select(value => value.GetInt32()).Should().Equal([255, 220, 0, 255]);

        JsonElement path = root.GetProperty("path");
        path.GetProperty("show").GetBoolean().Should().BeTrue();
        path.GetProperty("width").GetInt32().Should().Be(2);
        path.GetProperty("leadTime").GetInt32().Should().Be(0);
        path.GetProperty("trailTime").GetInt32().Should().Be(86400);
        path.GetProperty("material").GetProperty("solidColor").GetProperty("color").GetProperty("rgba")
            .EnumerateArray()
            .Select(value => value.GetInt32())
            .Should()
            .Equal([0, 200, 255, 220]);

        root.GetProperty("properties").GetProperty("orbitHint").GetProperty("string").GetString().Should().Be("900 km SSO / J2");
    }

    [Fact]
    public async Task CreateSsoJ2PacketAsync_ReturnsNullWithoutCallingJ2_WhenSsoFails()
    {
        var requestedPaths = new List<string>();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            requestedPaths.Add(request.RequestUri!.AbsolutePath);

            return Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "IsSuccess": false,
                  "Message": "no solution",
                  "Elements_Inertial": {
                    "SemimajorAxis": 0,
                    "Eccentricity": 0,
                    "Inclination": 0,
                    "ArgumentOfPeriapsis": 0,
                    "RightAscensionOfAscendingNode": 0,
                    "TrueAnomaly": 0,
                    "GravitationalParameter": 0
                  }
                }
                """));
        });

        var service = new OrbitScenarioService(CreateAstroxClient(handler));

        JsonElement? packet = await service.CreateSsoJ2PacketAsync(CreateScenario(), CancellationToken.None);

        packet.Should().BeNull();
        requestedPaths.Should().Equal("/OrbitWizard/SSO");
    }

    [Fact]
    public async Task CreateSsoJ2PacketAsync_ReturnsNull_WhenJ2Fails()
    {
        var requestedPaths = new List<string>();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            requestedPaths.Add(request.RequestUri!.AbsolutePath);

            if (request.RequestUri.AbsolutePath == "/OrbitWizard/SSO")
            {
                return Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, """
                    {
                      "IsSuccess": true,
                      "Message": "ok",
                      "Elements_Inertial": {
                        "SemimajorAxis": 7278136.3,
                        "Eccentricity": 0.001,
                        "Inclination": 98.9,
                        "ArgumentOfPeriapsis": 0,
                        "RightAscensionOfAscendingNode": 0,
                        "TrueAnomaly": 0,
                        "GravitationalParameter": 398600441800000
                      }
                    }
                    """));
            }

            return Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "IsSuccess": false,
                  "Message": "propagation failed",
                  "Position": {},
                  "Period": 0
                }
                """));
        });

        var service = new OrbitScenarioService(CreateAstroxClient(handler));

        JsonElement? packet = await service.CreateSsoJ2PacketAsync(CreateScenario(), CancellationToken.None);

        packet.Should().BeNull();
        requestedPaths.Should().Equal("/OrbitWizard/SSO", "/Propagator/J2");
    }

    [Theory]
    [MemberData(nameof(InvalidScenarios))]
    public async Task CreateSsoJ2PacketAsync_RejectsInvalidScenarioInput(SsoJ2Scenario scenario)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("Validation should short-circuit before any HTTP call."));
        var service = new OrbitScenarioService(CreateAstroxClient(handler));

        Func<Task> act = async () => await service.CreateSsoJ2PacketAsync(scenario, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public static TheoryData<SsoJ2Scenario> InvalidScenarios()
        => new()
        {
            CreateScenario() with { Id = "" },
            CreateScenario() with { Name = "" },
            CreateScenario() with { AltitudeKm = 99.9 },
            CreateScenario() with { AltitudeKm = 100000.1 },
            CreateScenario() with { Hours = 0 },
            CreateScenario() with { Hours = 24.1 },
            CreateScenario() with { StepSeconds = 0 },
            CreateScenario() with { StepSeconds = 3601 },
            CreateScenario() with { LocalTimeOfDescendingNode = -0.1 },
            CreateScenario() with { LocalTimeOfDescendingNode = 24 }
        };

    private static SsoJ2Scenario CreateScenario()
        => new(
            Id: "sso-900",
            Name: "SSO 900 km",
            AltitudeKm: 900,
            EpochUtc: DateTimeOffset.Parse("2026-07-16T00:00:00Z"),
            Hours: 24,
            StepSeconds: 60,
            LocalTimeOfDescendingNode: 10.5);

    private static AstroxClient CreateAstroxClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://astrox.example/")
        };

        return new AstroxClient(
            httpClient,
            Options.Create(new AstroxOptions
            {
                BaseUrl = new Uri("https://astrox.example/")
            }));
    }
}
