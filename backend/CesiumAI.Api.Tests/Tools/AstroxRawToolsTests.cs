using System.Net;
using CesiumAI.Api.Configuration;
using CesiumAI.Api.Tests.TestSupport;
using CesiumAI.Api.Tools;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CesiumAI.Api.Tests.Tools;

public class AstroxRawToolsTests
{
    [Fact]
    public void Constructor_RejectsHttpClientForNonAstroxOrigin()
    {
        var client = new HttpClient(new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("No request expected.")))
        {
            BaseAddress = new Uri("https://other.example/")
        };
        var options = Options.Create(new AstroxOptions
        {
            BaseUrl = new Uri("https://astrox.example/")
        });

        Action act = () => new AstroxRawTools(client, options);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*configured Astrox origin*");
    }

    [Theory]
    [InlineData("https://other.example/ssc")]
    [InlineData("/safe/../admin")]
    [InlineData("/safe/%2e%2e/admin")]
    [InlineData("ssc?sscName=ISS")]
    [InlineData("//other.example/ssc")]
    [InlineData("/safe\\..\\admin")]
    public async Task HttpGet_RejectsPathsThatCanEscapeAstrox(string path)
    {
        var tools = CreateTools(new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("Invalid paths must not send HTTP requests.")));

        Func<Task> act = () => tools.HttpGet(path, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task HttpGet_ReturnsStatusAndBody_FromAstroxRelativePath()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri.Should().Be(new Uri("https://astrox.example/ssc?sscName=ISS"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent("""{"message":"queued"}""")
            });
        });
        var tools = CreateTools(handler);

        AstroxRawResponse response = await tools.HttpGet("/ssc?sscName=ISS", CancellationToken.None);

        response.StatusCode.Should().Be(202);
        response.Body.Should().Be("""{"message":"queued"}""");
    }

    [Fact]
    public async Task HttpPost_SendsBodyAndReturnsNonSuccessStatusAndBody()
    {
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri.Should().Be(new Uri("https://astrox.example/Propagator/TwoBody"));
            request.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
            (await request.Content.ReadAsStringAsync(cancellationToken)).Should().Be("""{"id":"sat-1"}""");
            return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent("""{"message":"invalid orbit"}""")
            };
        });
        var tools = CreateTools(handler);

        AstroxRawResponse response = await tools.HttpPost(
            "/Propagator/TwoBody",
            """{"id":"sat-1"}""",
            CancellationToken.None);

        response.StatusCode.Should().Be(422);
        response.Body.Should().Be("""{"message":"invalid orbit"}""");
    }

    private static AstroxRawTools CreateTools(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var options = Options.Create(new AstroxOptions
        {
            BaseUrl = new Uri("https://astrox.example/api/")
        });

        return new AstroxRawTools(client, options);
    }
}
