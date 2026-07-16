using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using CesiumAI.Api.Configuration;
using Microsoft.Extensions.Options;

namespace CesiumAI.Api.Tools;

public sealed record AstroxRawResponse(int StatusCode, string Body);

public sealed class AstroxRawTools
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    public AstroxRawTools(HttpClient httpClient, IOptions<AstroxOptions> options)
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
        string decodedPath;

        try
        {
            decodedPath = Uri.UnescapeDataString(pathOnly);
        }
        catch (UriFormatException ex)
        {
            throw new ArgumentException("Astrox path contains invalid escaping.", nameof(path), ex);
        }

        if (decodedPath.Split('/').Any(segment => segment is ".."))
        {
            throw new ArgumentException("Astrox path traversal is not allowed.", nameof(path));
        }

        var requestUri = new Uri(_baseUri, path);
        if (!HasSameOrigin(requestUri, _baseUri))
        {
            throw new ArgumentException("Astrox path must stay on the configured Astrox origin.", nameof(path));
        }

        return requestUri;
    }

    private static bool HasSameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
        && left.Port == right.Port;
}
