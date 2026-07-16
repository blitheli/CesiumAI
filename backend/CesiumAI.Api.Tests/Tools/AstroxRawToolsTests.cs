using System.Net;
using System.Net.Sockets;
using System.Text;
using CesiumAI.Api.Configuration;
using CesiumAI.Api.Tests.TestSupport;
using CesiumAI.Api.Tools;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CesiumAI.Api.Tests.Tools;

public class AstroxRawToolsTests
{
    [Fact]
    public async Task HttpGet_PublicTransportReturnsRedirectWithoutFollowingExternalLocation()
    {
        using var astroxServer = new TcpListener(IPAddress.Loopback, 0);
        using var externalServer = new TcpListener(IPAddress.Any, 0);
        astroxServer.Start();
        externalServer.Start();
        int astroxRequests = 0;
        int externalRequests = 0;
        using var targetCancellation = new CancellationTokenSource();

        int externalPort = ((IPEndPoint)externalServer.LocalEndpoint).Port;
        var externalUri = new Uri($"http://127.0.0.2:{externalPort}/");
        Task astroxResponse = ServeOneAsync(
            astroxServer,
            () => Interlocked.Increment(ref astroxRequests),
            $"HTTP/1.1 302 Found\r\nLocation: {externalUri}outside\r\nContent-Type: text/plain\r\nContent-Length: 10\r\nConnection: close\r\n\r\nredirected",
            CancellationToken.None);
        Task externalResponse = ServeOneAsync(
            externalServer,
            () => Interlocked.Increment(ref externalRequests),
            "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 8\r\nConnection: close\r\n\r\nfollowed",
            targetCancellation.Token);
        var options = Options.Create(new AstroxOptions { BaseUrl = ServerUri(astroxServer) });
        using var tools = new AstroxRawTools(options);

        AstroxRawResponse response = await tools.HttpGet("/redirect", CancellationToken.None);
        await astroxResponse;
        await Task.Delay(50);
        targetCancellation.Cancel();
        await externalResponse;

        response.StatusCode.Should().Be(302);
        response.Body.Should().Be("redirected");
        astroxRequests.Should().Be(1);
        externalRequests.Should().Be(0);
    }

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
    [InlineData("/safe/%2e%2e%5cadmin")]
    [InlineData("/safe/%252e%252e%255cadmin")]
    [InlineData("/safe/%2e/admin")]
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

    private static Uri ServerUri(TcpListener listener)
    {
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        return new Uri($"http://127.0.0.1:{endpoint.Port}/");
    }

    private static async Task ServeOneAsync(
        TcpListener listener,
        Action onRequest,
        string response,
        CancellationToken cancellationToken)
    {
        try
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
            onRequest();
            await using NetworkStream stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true);

            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
            {
            }

            byte[] bytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(bytes, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
