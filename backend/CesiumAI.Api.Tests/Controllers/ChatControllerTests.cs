using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace CesiumAI.Api.Tests.Controllers;

public sealed class ChatControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task PostChat_ReturnsChatResponse()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/chat",
            CreateRequest("清空当前场景"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        body.RootElement.GetProperty("sessionId").GetString().Should().Be("test-session");
        body.RootElement.GetProperty("message").GetString().Should().Be("已清空场景。");
        JsonElement sceneOps = body.RootElement.GetProperty("sceneOps");
        sceneOps.GetArrayLength().Should().Be(1);
        sceneOps[0].GetProperty("op").GetString().Should().Be("clear");
    }

    [Fact]
    public async Task PostChat_WithWhitespaceMessage_ReturnsBadRequest()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/chat",
            CreateRequest("   "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostChat_WhenAgentTimesOut_ReturnsGatewayTimeout()
    {
        using HttpClient client = _factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(1);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/chat",
            CreateRequest("触发超时"));

        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        using JsonDocument body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        body.RootElement.GetProperty("error").GetString().Should().Be("agent_timeout");
        body.RootElement.GetProperty("detail").GetString()
            .Should().Be("Agent request exceeded 0.025 seconds.");
    }

    [Fact]
    public async Task PostChat_WhenServiceThrowsUnrelatedCancellation_DoesNotReturnAgentTimeout()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/chat",
            CreateRequest("触发无关取消"));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("agent_timeout");
    }

    [Fact]
    public async Task PostChat_WhenClientRequestIsCancelled_Returns499()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(CreateRequest("清空当前场景"))
        };
        request.Headers.Add("X-Test-Client-Cancelled", "true");

        HttpResponseMessage response = await _client.SendAsync(request);

        ((int)response.StatusCode).Should().Be(499);
    }

    [Fact]
    public async Task DevelopmentCors_AllowsViteOrigin()
    {
        using HttpRequestMessage request = CreatePreflightRequest("http://localhost:5173");

        HttpResponseMessage response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().ContainSingle("http://localhost:5173");
    }

    [Fact]
    public async Task DevelopmentCors_DoesNotAllowOtherOrigins()
    {
        using HttpRequestMessage request = CreatePreflightRequest("http://localhost:5174");

        HttpResponseMessage response = await _client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Production_DoesNotEnableCors()
    {
        using var productionFactory = new ApiFactory("Production");
        using HttpClient client = productionFactory.CreateClient();
        using HttpRequestMessage request = CreatePreflightRequest("http://localhost:5173");

        HttpResponseMessage response = await client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task WeatherForecastTemplateRoute_IsRemoved()
    {
        HttpResponseMessage response = await _client.GetAsync("/weatherforecast");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HealthCheck_ReturnsOkAfterStartupValidationSucceeds()
    {
        using HttpResponseMessage response = await _client.GetAsync("/healthz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static object CreateRequest(string message) =>
        new
        {
            message,
            sessionId = (string?)null,
            sceneSummary = new
            {
                entities = Array.Empty<object>()
            },
            relevantPackets = Array.Empty<object>()
        };

    private static HttpRequestMessage CreatePreflightRequest(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/chat");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        return request;
    }
}
