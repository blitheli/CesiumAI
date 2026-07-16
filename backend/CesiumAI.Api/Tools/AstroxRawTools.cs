using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using CesiumAI.Api.Configuration;
using Microsoft.Extensions.Options;

namespace CesiumAI.Api.Tools;

public sealed record AstroxRawResponse(int StatusCode, string Body);

public sealed class AstroxRawTools : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly bool _disposeHttpClient;

    public AstroxRawTools(IOptions<AstroxOptions> options)
        : this(CreateProductionHttpClient(options), options, disposeHttpClient: true)
    {
    }

    internal AstroxRawTools(HttpClient httpClient, IOptions<AstroxOptions> options)
        : this(httpClient, options, disposeHttpClient: false)
    {
    }

    private AstroxRawTools(
        HttpClient httpClient,
        IOptions<AstroxOptions> options,
        bool disposeHttpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _baseUri = options.Value.BaseUrl;
        if (!_baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Astrox base URL must be absolute.", nameof(options));
        }

        if (httpClient.BaseAddress is not null && !HasSameOrigin(httpClient.BaseAddress, _baseUri))
        {
            throw new ArgumentException("HTTP client base address must use the configured Astrox origin.", nameof(httpClient));
        }

        httpClient.BaseAddress ??= _baseUri;
        _httpClient = httpClient;
        _disposeHttpClient = disposeHttpClient;
    }

    [Description("Send a GET request to a path on the configured Astrox service.")]
    public async Task<AstroxRawResponse> HttpGet(
        string path,
        CancellationToken cancellationToken = default)
    {
        Uri requestUri = ValidateAndResolvePath(path);
        using HttpResponseMessage response = await _httpClient.GetAsync(requestUri, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new AstroxRawResponse((int)response.StatusCode, body);
    }

    [Description("Send a JSON POST request to a path on the configured Astrox service.")]
    public async Task<AstroxRawResponse> HttpPost(
        string path,
        string body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        Uri requestUri = ValidateAndResolvePath(path);
        using var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using HttpResponseMessage response = await _httpClient.PostAsync(requestUri, content, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return new AstroxRawResponse((int)response.StatusCode, responseBody);
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private Uri ValidateAndResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path[0] != '/'
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains('\\'))
        {
            throw new ArgumentException("Astrox path must be a root-relative path beginning with a single '/'.", nameof(path));
        }

        int suffixStart = path.IndexOfAny(['?', '#']);
        string pathOnly = suffixStart < 0 ? path : path[..suffixStart];
        ValidateDecodedPath(pathOnly, nameof(path));

        var requestUri = new Uri(_baseUri, path);
        if (!HasSameOrigin(requestUri, _baseUri))
        {
            throw new ArgumentException("Astrox path must stay on the configured Astrox origin.", nameof(path));
        }

        return requestUri;
    }

    private static void ValidateDecodedPath(string path, string parameterName)
    {
        const int maximumDecodeRounds = 4;
        string current = path;

        for (int round = 0; round <= maximumDecodeRounds; round++)
        {
            if (current.Contains('\\')
                || current.StartsWith("//", StringComparison.Ordinal)
                || current.Split(['/', '\\']).Any(segment => segment is "." or ".."))
            {
                throw new ArgumentException(
                    "Astrox path cannot contain backslashes or dot segments.",
                    parameterName);
            }

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(current);
            }
            catch (UriFormatException ex)
            {
                throw new ArgumentException("Astrox path contains invalid escaping.", parameterName, ex);
            }

            if (string.Equals(decoded, current, StringComparison.Ordinal))
            {
                return;
            }

            current = decoded;
        }

        throw new ArgumentException(
            "Astrox path exceeds the supported URL-decoding depth.",
            parameterName);
    }

    private static bool HasSameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
        && left.Port == right.Port;

    private static HttpClient CreateProductionHttpClient(IOptions<AstroxOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false
        });
    }
}
