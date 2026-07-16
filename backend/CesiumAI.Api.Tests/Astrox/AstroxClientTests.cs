using System.Net;
using System.Text;
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
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();

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

            return StubHttpMessageHandler.Json(HttpStatusCode.OK, responseJson);
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

        capturedBody.Should().NotBeNull();
        using JsonDocument document = JsonDocument.Parse(capturedBody!);
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
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();

            const string responseJson = """
                {
                  "IsSuccess": true,
                  "Message": "ok",
                  "Position": {
                    "epoch": "2026-07-16T00:00:00.000Z",
                    "cartesianVelocity": [0, 1, 2, 3, 4, 5, 6]
                  },
                  "Period": 6000
                }
                """;

            return StubHttpMessageHandler.Json(HttpStatusCode.OK, responseJson);
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

        capturedBody.Should().NotBeNull();
        using JsonDocument document = JsonDocument.Parse(capturedBody!);
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

    [Theory]
    [MemberData(nameof(InvalidSuccessfulSsoBodies))]
    public async Task CreateSsoAsync_RejectsIncompleteOrNonFiniteSuccessfulPayload(
        string responseJson)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                responseJson)));
        var client = CreateClient(handler);

        Func<Task> act = async () => await client.CreateSsoAsync(
            CreateSsoRequest(),
            CancellationToken.None);

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("*OrbitWizard/SSO*invalid*payload*");
    }

    [Theory]
    [MemberData(nameof(InvalidSuccessfulJ2Bodies))]
    public async Task PropagateJ2Async_RejectsMalformedSuccessfulPosition(
        string responseJson)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                responseJson)));
        var client = CreateClient(handler);

        Func<Task> act = async () => await client.PropagateJ2Async(
            CreateJ2Request(),
            CancellationToken.None);

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("*Propagator/J2*invalid*payload*");
    }

    [Theory]
    [InlineData("cartesian", 4)]
    [InlineData("cartesianVelocity", 7)]
    public async Task PropagateJ2Async_AcceptsFinitePositionSamplesWithExpectedStride(
        string propertyName,
        int stride)
    {
        string samples = string.Join(",", Enumerable.Range(0, stride));
        string responseJson = $$"""
            {
              "IsSuccess": true,
              "Message": "ok",
              "Position": {
                "epoch": "2026-07-16T00:00:00.000Z",
                "{{propertyName}}": [{{samples}}]
              },
              "Period": 6000
            }
            """;
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                responseJson)));
        var client = CreateClient(handler);

        J2Response response = await client.PropagateJ2Async(
            CreateJ2Request(),
            CancellationToken.None);

        response.Position.GetProperty(propertyName).GetArrayLength()
            .Should().Be(stride);
    }

    [Theory]
    [InlineData("/Propagator/TwoBody")]
    [InlineData("/Propagator/SGP4")]
    public async Task PropagateAsync_PostsRequestJsonAsIs_AndPositionSurvivesResponseDisposal(
        string endpoint)
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedRequestBody = null;
        const string requestJson = """{"Start":"2026-07-16T00:00:00.000Z","Step":60}""";
        using JsonDocument requestDocument = JsonDocument.Parse(requestJson);
        const string responseJson = """
            {
              "IsSuccess": true,
              "Message": "ok",
              "Position": {
                "epoch": "2026-07-16T00:00:00.000Z",
                "cartesian": [0, 1, 2, 3]
              },
              "Period": 5400
            }
            """;
        var responseContent = new TrackingStringContent(responseJson);
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            capturedRequest = request;
            capturedRequestBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = responseContent
            };
        });
        var client = CreateClient(handler);

        GenericPropagationResponse response = await client.PropagateAsync(
            endpoint,
            requestDocument.RootElement,
            CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri.Should().Be(new Uri($"https://astrox.example{endpoint}"));
        capturedRequestBody.Should().Be(requestJson);

        responseContent.IsDisposed.Should().BeTrue();
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("ok");
        response.Period.Should().Be(5400);
        // HTTP 响应内容已释放后，返回的 Position 仍须完整可读。
        response.Position.GetProperty("epoch").GetString().Should().Be("2026-07-16T00:00:00.000Z");
        response.Position.GetProperty("cartesian")
            .EnumerateArray()
            .Select(value => value.GetDouble())
            .Should()
            .Equal([0, 1, 2, 3]);
    }

    [Theory]
    [MemberData(nameof(DuplicateOrWrongCasedAstroxRootBodies))]
    public async Task PropagateAsync_RejectsDuplicateOrWrongCasedCanonicalRootKeys(
        string responseJson)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, responseJson)));
        var client = CreateClient(handler);
        using JsonDocument requestDocument = JsonDocument.Parse("""{"Step":60}""");

        Func<Task> act = async () => await client.PropagateAsync(
            "/Propagator/TwoBody",
            requestDocument.RootElement,
            CancellationToken.None);

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("*invalid*payload*");
    }

    [Theory]
    [InlineData("""
        {
          "IsSuccess": true,
          "Message": "ok",
          "Position": {
            "epoch": "2026-07-16T00:00:00.000Z",
            "Cartesian": [0, 1, 2, 3]
          },
          "Period": 6000
        }
        """)]
    [InlineData("""
        {
          "IsSuccess": true,
          "Message": "ok",
          "Position": {
            "epoch": "2026-07-16T00:00:00.000Z",
            "cartesian": [0, 1, 2, 3],
            "Cartesian": [9, 9, 9, 9]
          },
          "Period": 6000
        }
        """)]
    public async Task PropagateAsync_RejectsWrongCasedOrDuplicatePositionKeys(
        string responseJson)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, responseJson)));
        var client = CreateClient(handler);
        using JsonDocument requestDocument = JsonDocument.Parse("""{"Step":60}""");

        Func<Task> act = async () => await client.PropagateAsync(
            "/Propagator/TwoBody",
            requestDocument.RootElement,
            CancellationToken.None);

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("*invalid*payload*");
    }

    [Fact]
    public async Task PropagateAsync_RejectsOversizedContentLength_BeforeReadingBody()
    {
        var unreadStream = new UnreadableStream();
        var content = new StreamContent(unreadStream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        content.Headers.ContentLength = AstroxClient.MaxGenericResponseBytes + 1;

        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var client = CreateClient(handler);
        using JsonDocument requestDocument = JsonDocument.Parse("""{"Step":60}""");

        Func<Task> act = async () => await client.PropagateAsync(
            "/Propagator/TwoBody",
            requestDocument.RootElement,
            CancellationToken.None);

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("*response*too large*");
        unreadStream.ReadAttempted.Should().BeFalse();
    }

    [Fact]
    public async Task PropagateAsync_RejectsOversizedBody_WhenContentLengthIsAbsent()
    {
        byte[] oversized = Encoding.UTF8.GetBytes(
            new string('a', AstroxClient.MaxGenericResponseBytes + 1));
        var content = new StreamContent(new NonSeekableMemoryStream(oversized));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        content.Headers.ContentLength = null;

        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var client = CreateClient(handler);
        using JsonDocument requestDocument = JsonDocument.Parse("""{"Step":60}""");

        Func<Task> act = async () => await client.PropagateAsync(
            "/Propagator/TwoBody",
            requestDocument.RootElement,
            CancellationToken.None);

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("*response*too large*");
    }

    [Fact]
    public async Task PropagateAsync_RejectsResponseLargerThanGenericLimit_WhileJ2AcceptsSameSize()
    {
        string largeBody = CreateValidPropagationResponseLargerThan(
            AstroxClient.MaxGenericResponseBytes);
        largeBody.Length.Should().BeGreaterThan(AstroxClient.MaxGenericResponseBytes);
        largeBody.Length.Should().BeLessThanOrEqualTo(AstroxClient.MaxTypedResponseBytes);

        var genericHandler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, largeBody)));
        var genericClient = CreateClient(genericHandler);
        using JsonDocument requestDocument = JsonDocument.Parse("""{"Step":60}""");

        Func<Task> genericAct = async () => await genericClient.PropagateAsync(
            "/Propagator/TwoBody",
            requestDocument.RootElement,
            CancellationToken.None);

        await genericAct.Should().ThrowAsync<AstroxException>()
            .WithMessage("*response*too large*");

        var j2Handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, largeBody)));
        var j2Client = CreateClient(j2Handler);

        J2Response j2Response = await j2Client.PropagateJ2Async(
            CreateJ2Request(),
            CancellationToken.None);

        j2Response.Position.GetProperty("cartesian")
            .EnumerateArray()
            .Select(value => value.GetDouble())
            .Should()
            .Equal([0, 1, 2, 3]);
    }

    [Fact]
    public async Task PropagateAsync_DisposesRequestContent_OnSuccessAndOnHttpFailure()
    {
        HttpRequestMessage? successRequest = null;
        var successHandler = new StubHttpMessageHandler((request, _) =>
        {
            successRequest = request;
            return Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, """
                {
                  "IsSuccess": true,
                  "Message": "ok",
                  "Position": {
                    "epoch": "2026-07-16T00:00:00.000Z",
                    "cartesian": [0, 1, 2, 3]
                  },
                  "Period": 5400
                }
                """));
        });
        using JsonDocument requestDocument = JsonDocument.Parse("""{"Step":60}""");
        await CreateClient(successHandler).PropagateAsync(
            "/Propagator/TwoBody",
            requestDocument.RootElement,
            CancellationToken.None);

        successRequest.Should().NotBeNull();
        Func<Task> readSuccessContent = () => successRequest!.Content!.ReadAsStringAsync();
        await readSuccessContent.Should().ThrowAsync<ObjectDisposedException>();

        HttpRequestMessage? failureRequest = null;
        var failureHandler = new StubHttpMessageHandler((request, _) =>
        {
            failureRequest = request;
            return Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.BadGateway, """
                { "Message": "gateway unavailable" }
                """));
        });

        Func<Task> failureAct = async () => await CreateClient(failureHandler).PropagateAsync(
            "/Propagator/TwoBody",
            requestDocument.RootElement,
            CancellationToken.None);

        await failureAct.Should().ThrowAsync<AstroxException>();
        failureRequest.Should().NotBeNull();
        Func<Task> readFailureContent = () => failureRequest!.Content!.ReadAsStringAsync();
        await readFailureContent.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task CreateSsoAsync_DisposesRequestContent_OnSuccess()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
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
        });

        await CreateClient(handler).CreateSsoAsync(CreateSsoRequest(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        Func<Task> readContent = () => capturedRequest!.Content!.ReadAsStringAsync();
        await readContent.Should().ThrowAsync<ObjectDisposedException>();
    }

    public static TheoryData<string> DuplicateOrWrongCasedAstroxRootBodies()
        => new()
        {
            // 验证首个 Position、反序列化可能吃到最后一个 position 的绕过。
            """
            {
              "IsSuccess": true,
              "Message": "ok",
              "Position": {
                "epoch": "2026-07-16T00:00:00.000Z",
                "cartesian": [0, 1, 2, 3]
              },
              "position": {
                "epoch": "2026-07-16T00:00:00.000Z",
                "cartesian": [9, 9, 9, 9]
              },
              "Period": 6000
            }
            """,
            """
            {
              "IsSuccess": true,
              "issuccess": false,
              "Message": "ok",
              "Position": {
                "epoch": "2026-07-16T00:00:00.000Z",
                "cartesian": [0, 1, 2, 3]
              },
              "Period": 6000
            }
            """,
            """
            {
              "IsSuccess": true,
              "Message": "ok",
              "message": "evil",
              "Position": {
                "epoch": "2026-07-16T00:00:00.000Z",
                "cartesian": [0, 1, 2, 3]
              },
              "Period": 6000
            }
            """,
            """
            {
              "IsSuccess": true,
              "Message": "ok",
              "Position": {
                "epoch": "2026-07-16T00:00:00.000Z",
                "cartesian": [0, 1, 2, 3]
              },
              "Period": 6000,
              "period": 1
            }
            """,
            """
            {
              "issuccess": true,
              "Message": "ok",
              "Position": {
                "epoch": "2026-07-16T00:00:00.000Z",
                "cartesian": [0, 1, 2, 3]
              },
              "Period": 6000
            }
            """
        };

    [Theory]
    [InlineData("https://evil.example/Propagator/J2")]
    [InlineData("http://astrox.example/Propagator/J2")]
    [InlineData("//evil.example/Propagator/J2")]
    [InlineData("/Propagator/../OrbitWizard/SSO")]
    [InlineData("/Propagator/%2e%2e/OrbitWizard/SSO")]
    [InlineData("/Propagator/%2e%2e%2fOrbitWizard/SSO")]
    [InlineData("/Propagator/%252e%252e/OrbitWizard/SSO")]
    [InlineData("/Propagator/J2/../../OrbitWizard/SSO")]
    [InlineData("/Propagator/J2#fragment")]
    [InlineData("/Propagator/J2?next=/OrbitWizard/SSO")]
    [InlineData("/OrbitWizard/SSO")]
    [InlineData("/propagator/J2")]
    [InlineData("Propagator/J2")]
    [InlineData("/Propagator\\J2")]
    [InlineData("/Propagator/%2e/J2")]
    [InlineData("")]
    [InlineData(" ")]
    public async Task PropagateAsync_RejectsUnsafeOrNonPropagatorEndpoints(
        string endpoint)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("Unsafe endpoints must not send HTTP requests."));
        var client = CreateClient(handler);
        using JsonDocument requestDocument = JsonDocument.Parse("""{"Step":60}""");

        Func<Task> act = async () => await client.PropagateAsync(
            endpoint,
            requestDocument.RootElement,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PropagateAsync_ThrowsAstroxException_WhenHttpStatusIsNotSuccessful()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.BadGateway, """
                {
                  "Message": "gateway unavailable"
                }
                """)));
        var client = CreateClient(handler);
        using JsonDocument requestDocument = JsonDocument.Parse("""{"Step":60}""");

        Func<Task> act = async () => await client.PropagateAsync(
            "/Propagator/TwoBody",
            requestDocument.RootElement,
            CancellationToken.None);

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("*Propagator/TwoBody*gateway unavailable*");
    }

    [Fact]
    public async Task PropagateAsync_ThrowsAstroxException_WhenResponseBodyIsEmpty()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty)
            }));
        var client = CreateClient(handler);
        using JsonDocument requestDocument = JsonDocument.Parse("""{"Step":60}""");

        Func<Task> act = async () => await client.PropagateAsync(
            "/Propagator/TwoBody",
            requestDocument.RootElement,
            CancellationToken.None);

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("*Propagator/TwoBody*empty response body*");
    }

    [Fact]
    public async Task PropagateAsync_ThrowsAstroxException_WhenResponseBodyIsInvalidJson()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json")
            }));
        var client = CreateClient(handler);
        using JsonDocument requestDocument = JsonDocument.Parse("""{"Step":60}""");

        Func<Task> act = async () => await client.PropagateAsync(
            "/Propagator/TwoBody",
            requestDocument.RootElement,
            CancellationToken.None);

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("*Propagator/TwoBody*invalid JSON*");
    }

    [Fact]
    public async Task PropagateAsync_ThrowsAstroxException_WhenAstroxRejectsTheRequest()
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
        using JsonDocument requestDocument = JsonDocument.Parse("""{"Step":60}""");

        Func<Task> act = async () => await client.PropagateAsync(
            "/Propagator/TwoBody",
            requestDocument.RootElement,
            CancellationToken.None);

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("*Propagator/TwoBody*orbit rejected*");
    }

    [Theory]
    [MemberData(nameof(InvalidSuccessfulGenericPropagationBodies))]
    public async Task PropagateAsync_RejectsMissingOrMalformedPosition(
        string responseJson)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, responseJson)));
        var client = CreateClient(handler);
        using JsonDocument requestDocument = JsonDocument.Parse("""{"Step":60}""");

        Func<Task> act = async () => await client.PropagateAsync(
            "/Propagator/TwoBody",
            requestDocument.RootElement,
            CancellationToken.None);

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("*Propagator/TwoBody*invalid*payload*");
    }

    public static TheoryData<string> InvalidSuccessfulGenericPropagationBodies()
        => new()
        {
            """
            { "IsSuccess": true, "Message": "ok", "Period": 6000 }
            """,
            """
            { "IsSuccess": true, "Message": "ok", "Position": null, "Period": 6000 }
            """,
            """
            { "IsSuccess": true, "Message": "ok", "Position": [], "Period": 6000 }
            """,
            """
            { "IsSuccess": true, "Message": "ok", "Position": {}, "Period": 6000 }
            """,
            """
            {
              "IsSuccess": true,
              "Message": "ok",
              "Position": { "epoch": "", "cartesian": [0, 1, 2, 3] },
              "Period": 6000
            }
            """,
            """
            {
              "IsSuccess": true,
              "Message": "ok",
              "Position": {
                "epoch": "2026-07-16T00:00:00.000Z",
                "cartesian": [0, 1e400, 2, 3]
              },
              "Period": 6000
            }
            """
        };

    [Fact]
    public async Task SuccessfulResponse_IsDisposedDeterministically()
    {
        var content = new TrackingStringContent("""
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
            """);
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            }));
        var client = CreateClient(handler);

        await client.CreateSsoAsync(CreateSsoRequest(), CancellationToken.None);

        content.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task CreateSsoAsync_RejectsDuplicateCaseVariantElementsInertialRootKey()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, """
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
                  },
                  "elements_inertial": {
                    "SemimajorAxis": 1,
                    "Eccentricity": 0,
                    "Inclination": 0,
                    "ArgumentOfPeriapsis": 0,
                    "RightAscensionOfAscendingNode": 0,
                    "TrueAnomaly": 0,
                    "GravitationalParameter": 1
                  }
                }
                """)));
        var client = CreateClient(handler);

        Func<Task> act = async () => await client.CreateSsoAsync(
            CreateSsoRequest(),
            CancellationToken.None);

        await act.Should().ThrowAsync<AstroxException>()
            .WithMessage("*invalid*payload*");
    }

    public static TheoryData<string> InvalidSuccessfulSsoBodies()
        => new()
        {
            """
            { "IsSuccess": true, "Message": "ok" }
            """,
            """
            { "IsSuccess": true, "Message": "ok", "Elements_Inertial": null }
            """,
            """
            { "IsSuccess": true, "Message": "ok", "Elements_Inertial": {} }
            """,
            """
            {
              "IsSuccess": true,
              "Message": "ok",
              "Elements_Inertial": {
                "SemimajorAxis": 7278136.3,
                "Eccentricity": 0.001,
                "Inclination": 98.9,
                "ArgumentOfPeriapsis": 0,
                "RightAscensionOfAscendingNode": 0,
                "TrueAnomaly": 0
              }
            }
            """,
            """
            {
              "IsSuccess": true,
              "Message": "ok",
              "Elements_Inertial": {
                "SemimajorAxis": "not-a-number",
                "Eccentricity": 0.001,
                "Inclination": 98.9,
                "ArgumentOfPeriapsis": 0,
                "RightAscensionOfAscendingNode": 0,
                "TrueAnomaly": 0,
                "GravitationalParameter": 398600441800000
              }
            }
            """,
            """
            {
              "IsSuccess": true,
              "Message": "ok",
              "Elements_Inertial": {
                "SemimajorAxis": 1e400,
                "Eccentricity": 0.001,
                "Inclination": 98.9,
                "ArgumentOfPeriapsis": 0,
                "RightAscensionOfAscendingNode": 0,
                "TrueAnomaly": 0,
                "GravitationalParameter": 398600441800000
              }
            }
            """
        };

    public static TheoryData<string> InvalidSuccessfulJ2Bodies()
        => new()
        {
            """
            { "IsSuccess": true, "Message": "ok", "Period": 6000 }
            """,
            """
            { "IsSuccess": true, "Message": "ok", "Position": null, "Period": 6000 }
            """,
            """
            { "IsSuccess": true, "Message": "ok", "Position": [], "Period": 6000 }
            """,
            """
            { "IsSuccess": true, "Message": "ok", "Position": {}, "Period": 6000 }
            """,
            """
            {
              "IsSuccess": true,
              "Message": "ok",
              "Position": { "epoch": "", "cartesian": [0, 1, 2, 3] },
              "Period": 6000
            }
            """,
            """
            {
              "IsSuccess": true,
              "Message": "ok",
              "Position": {
                "epoch": "2026-07-16T00:00:00.000Z",
                "cartesian": []
              },
              "Period": 6000
            }
            """,
            """
            {
              "IsSuccess": true,
              "Message": "ok",
              "Position": {
                "epoch": "2026-07-16T00:00:00.000Z",
                "cartesian": [0, 1, 2]
              },
              "Period": 6000
            }
            """,
            """
            {
              "IsSuccess": true,
              "Message": "ok",
              "Position": {
                "epoch": "2026-07-16T00:00:00.000Z",
                "cartesianVelocity": [0, 1, 2, 3, 4, 5]
              },
              "Period": 6000
            }
            """,
            """
            {
              "IsSuccess": true,
              "Message": "ok",
              "Position": {
                "epoch": "2026-07-16T00:00:00.000Z",
                "cartesian": [0, 1, "bad", 3]
              },
              "Period": 6000
            }
            """,
            """
            {
              "IsSuccess": true,
              "Message": "ok",
              "Position": {
                "epoch": "2026-07-16T00:00:00.000Z",
                "cartesian": [0, 1e400, 2, 3]
              },
              "Period": 6000
            }
            """,
            """
            {
              "IsSuccess": true,
              "Message": "ok",
              "Position": {
                "cartesianVelocity": {
                  "epoch": "2026-07-16T00:00:00.000Z",
                  "cartesian": [0, 1, 2, 3, 4, 5, 6]
                }
              },
              "Period": 6000
            }
            """
        };

    private static string CreateValidPropagationResponseLargerThan(int minimumBytes)
    {
        const string prefix =
            "{\"IsSuccess\":true,\"Message\":\"ok\",\"Position\":{\"epoch\":\"2026-07-16T00:00:00.000Z\",\"cartesian\":[0,1,2,3]},\"Period\":6000,\"pad\":\"";
        const string suffix = "\"}";
        int padLength = Math.Max(1, minimumBytes - prefix.Length - suffix.Length + 1);
        return prefix + new string('x', padLength) + suffix;
    }

    private static SsoRequest CreateSsoRequest()
        => new(
            Description: "SSO-900",
            OrbitEpoch: DateTimeOffset.Parse("2026-07-16T00:00:00.000Z"),
            Altitude: 900,
            LocalTimeOfDescendingNode: 10.5);

    private static J2Request CreateJ2Request()
        => new(
            Start: DateTimeOffset.Parse("2026-07-16T00:00:00.000Z"),
            Stop: DateTimeOffset.Parse("2026-07-17T00:00:00.000Z"),
            CentralBody: "Earth",
            OrbitEpoch: DateTimeOffset.Parse("2026-07-16T00:00:00.000Z"),
            CoordType: "Classical",
            OrbitalElements: [7278136.3, 0.001, 98.9, 0, 0, 0],
            Step: 60);

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

    private sealed class TrackingStringContent(string content) : StringContent(content)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 不可读流：用于断言 Content-Length 超限时不会读取 body。
    /// </summary>
    private sealed class UnreadableStream : Stream
    {
        public bool ReadAttempted { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadAttempted = true;
            throw new InvalidOperationException("Body must not be read when Content-Length exceeds the limit.");
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// 非 seekable 流，避免 StreamContent 自动填充 Content-Length。
    /// </summary>
    private sealed class NonSeekableMemoryStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
