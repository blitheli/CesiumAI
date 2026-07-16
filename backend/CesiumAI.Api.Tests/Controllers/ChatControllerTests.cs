using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace CesiumAI.Api.Tests.Controllers;

public sealed class ChatControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
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
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/chat",
            CreateRequest("触发超时"));

        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        using JsonDocument body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        body.RootElement.GetProperty("error").GetString().Should().Be("agent_timeout");
        body.RootElement.GetProperty("detail").GetString()
            .Should().Be("Agent request exceeded 120 seconds.");
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
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/chat");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        HttpResponseMessage response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().ContainSingle("http://localhost:5173");
    }

    [Fact]
    public async Task WeatherForecastTemplateRoute_IsRemoved()
    {
        HttpResponseMessage response = await _client.GetAsync("/weatherforecast");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
}
