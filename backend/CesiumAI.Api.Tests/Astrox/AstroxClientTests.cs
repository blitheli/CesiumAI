using System.Net;
using System.Text.Json;
using CesiumAI.Api.Astrox;
using CesiumAI.Api.Configuration;
using CesiumAI.Api.Tests.TestSupport;
using FluentAssertions;

namespace CesiumAI.Api.Tests.Astrox;

public class AstroxClientTests
{
    [Fact]
    public async Task CreateSsoAsync_PostsPascalCasePayloadToOrbitWizardSso()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;

            const string responseJson = """
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
                """;

            return Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, responseJson));
        });
        var client = CreateClient(handler);

        await client.CreateSsoAsync(
            new SsoRequest(
                Description: "SSO-900",
                OrbitEpoch: DateTimeOffset.Parse("2026-07-16T00:00:00.000Z"),
                Altitude: 900,
                LocalTimeOfDescendingNode: 10.5),
            CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri.Should().Be(new Uri("https://astrox.example/OrbitWizard/SSO"));

        string body = await capturedRequest.Content!.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;

        root.TryGetProperty("Description", out JsonElement description).Should().BeTrue();
        description.GetString().Should().Be("SSO-900");
        root.TryGetProperty("OrbitEpoch", out JsonElement orbitEpoch).Should().BeTrue();
        orbitEpoch.GetString().Should().Be("2026-07-16T00:00:00.000Z");
        root.TryGetProperty("Altitude", out JsonElement altitude).Should().BeTrue();
        altitude.GetDouble().Should().Be(900);
        root.TryGetProperty("LocalTimeOfDescendingNode", out JsonElement ltdn).Should().BeTrue();
        ltdn.GetDouble().Should().Be(10.5);
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["Description", "OrbitEpoch", "Altitude", "LocalTimeOfDescendingNode"]);
    }

    [Fact]
    public async Task PropagateJ2Async_PostsPascalCasePayloadToPropagatorJ2()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;

            const string responseJson = """
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
                """;

            return Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, responseJson));
        });
        var client = CreateClient(handler);

        await client.PropagateJ2Async(
            new J2Request(
                Start: DateTimeOffset.Parse("2026-07-16T00:00:00.000Z"),
                Stop: DateTimeOffset.Parse("2026-07-17T00:00:00.000Z"),
                CentralBody: "Earth",
                OrbitEpoch: DateTimeOffset.Parse("2026-07-16T00:00:00.000Z"),
                CoordType: "Classical",
                OrbitalElements: [7278136.3, 0.001, 98.9, 0, 0, 0],
                Step: 60),
            CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri.Should().Be(new Uri("https://astrox.example/Propagator/J2"));

        string body = await capturedRequest.Content!.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;

        root.TryGetProperty("Start", out JsonElement start).Should().BeTrue();
        start.GetString().Should().Be("2026-07-16T00:00:00.000Z");
        root.TryGetProperty("Stop", out JsonElement stop).Should().BeTrue();
        stop.GetString().Should().Be("2026-07-17T00:00:00.000Z");
        root.TryGetProperty("CentralBody", out JsonElement centralBody).Should().BeTrue();
        centralBody.GetString().Should().Be("Earth");
        root.TryGetProperty("OrbitEpoch", out JsonElement orbitEpoch).Should().BeTrue();
        orbitEpoch.GetString().Should().Be("2026-07-16T00:00:00.000Z");
        root.TryGetProperty("CoordType", out JsonElement coordType).Should().BeTrue();
        coordType.GetString().Should().Be("Classical");
        root.TryGetProperty("OrbitalElements", out JsonElement orbitalElements).Should().BeTrue();
        orbitalElements.EnumerateArray().Select(element => element.GetDouble()).Should().Equal([7278136.3, 0.001, 98.9, 0, 0, 0]);
        root.TryGetProperty("Step", out JsonElement step).Should().BeTrue();
        step.GetInt32().Should().Be(60);
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["Start", "Stop", "CentralBody", "OrbitEpoch", "CoordType", "OrbitalElements", "Step"]);
    }

    [Fact]
    public async Task CreateSsoAsync_ThrowsAstroxException_WhenHttpStatusIsNotSuccessful()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.BadGateway, """
                {
                  "Message": "gateway unavailable"
                }
                """)));
        var client = CreateClient(handler);

        Func<Task> act = async () => await client.CreateSsoAsync(
            new SsoRequest(
                Description: "SSO-900",
                OrbitEpoch: DateTimeOffset.Parse("2026-07-16T00:00:00.000Z"),
                Altitude: 900,
                LocalTimeOfDescendingNode: 10.5),
            CancellationToken.None);

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("*OrbitWizard/SSO*gateway unavailable*");
    }

    [Fact]
    public async Task PropagateJ2Async_ThrowsAstroxException_WhenAstroxRejectsTheRequest()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "IsSuccess": false,
                  "Message": "orbit rejected",
                  "Position": {},
                  "Period": 0
                }
                """)));
        var client = CreateClient(handler);

        Func<Task> act = async () => await client.PropagateJ2Async(
            new J2Request(
                Start: DateTimeOffset.Parse("2026-07-16T00:00:00.000Z"),
                Stop: DateTimeOffset.Parse("2026-07-17T00:00:00.000Z"),
                CentralBody: "Earth",
                OrbitEpoch: DateTimeOffset.Parse("2026-07-16T00:00:00.000Z"),
                CoordType: "Classical",
                OrbitalElements: [7278136.3, 0.001, 98.9, 0, 0, 0],
                Step: 60),
            CancellationToken.None);

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("*Propagator/J2*orbit rejected*");
    }

    [Fact]
    public async Task CreateSsoAsync_ThrowsAstroxException_WhenResponseBodyIsWhitespace()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("   ")
            }));
        var client = CreateClient(handler);

        Func<Task> act = async () => await client.CreateSsoAsync(
            new SsoRequest(
                Description: "SSO-900",
                OrbitEpoch: DateTimeOffset.Parse("2026-07-16T00:00:00.000Z"),
                Altitude: 900,
                LocalTimeOfDescendingNode: 10.5),
            CancellationToken.None);

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("*OrbitWizard/SSO*empty response body*");
    }

    [Fact]
    public async Task PropagateJ2Async_ThrowsAstroxException_WhenResponseBodyIsEmpty()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty)
            }));
        var client = CreateClient(handler);

        Func<Task> act = async () => await client.PropagateJ2Async(
            new J2Request(
                Start: DateTimeOffset.Parse("2026-07-16T00:00:00.000Z"),
                Stop: DateTimeOffset.Parse("2026-07-17T00:00:00.000Z"),
                CentralBody: "Earth",
                OrbitEpoch: DateTimeOffset.Parse("2026-07-16T00:00:00.000Z"),
                CoordType: "Classical",
                OrbitalElements: [7278136.3, 0.001, 98.9, 0, 0, 0],
                Step: 60),
            CancellationToken.None);

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("*Propagator/J2*empty response body*");
    }

    private static AstroxClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://astrox.example/")
        };

        return new AstroxClient(
            httpClient,
            Microsoft.Extensions.Options.Options.Create(new AstroxOptions
            {
                BaseUrl = new Uri("https://astrox.example/")
            }));
    }
}
