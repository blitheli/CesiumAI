using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CesiumAI.Api.Tests;

public class ApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiSmokeTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HostStarts_AndWeatherForecastRouteIsAccessible()
    {
        var response = await _client.GetAsync("/weatherforecast");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
