using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CesiumAI.Api.Configuration;
using Microsoft.Extensions.Options;

namespace CesiumAI.Api.Astrox;

public sealed class AstroxClient : IAstroxClient
{
    /// <summary>
    /// 通用 PropagateAsync 响应体上限：2 MiB Position + 64 KiB envelope。
    /// </summary>
    public const int MaxGenericResponseBytes = (2 * 1024 * 1024) + (64 * 1024);

    /// <summary>
    /// 旧 SSO/J2 typed 路径响应体安全上限，足以容纳既有 24h/1s 步长合法输出。
    /// </summary>
    public const int MaxTypedResponseBytes = 64 * 1024 * 1024;

    private const string PropagatorPrefix = "/Propagator/";

    private static readonly string[] CanonicalRootKeys =
    [
        "IsSuccess",
        "Message",
        "Position",
        "Period",
        "Elements_Inertial"
    ];

    private static readonly JsonSerializerOptions RequestJsonOptions = CreateRequestOptions();
    private static readonly JsonSerializerOptions ResponseJsonOptions = CreateResponseOptions();

    private readonly HttpClient _httpClient;

    public AstroxClient(HttpClient httpClient, IOptions<AstroxOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        AstroxOptions astroxOptions = options.Value;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= astroxOptions.BaseUrl;
    }

    public Task<SsoResponse> CreateSsoAsync(SsoRequest request, CancellationToken cancellationToken)
        => PostAsync<SsoRequest, SsoResponse>("/OrbitWizard/SSO", request, cancellationToken);

    public Task<J2Response> PropagateJ2Async(J2Request request, CancellationToken cancellationToken)
        => PostAsync<J2Request, J2Response>("/Propagator/J2", request, cancellationToken);

    public async Task<GenericPropagationResponse> PropagateAsync(
        string endpoint,
        JsonElement request,
        CancellationToken cancellationToken)
    {
        string normalizedEndpoint = NormalizePropagatorEndpoint(endpoint);
        GenericPropagationResponse response = await PostContentAsync<GenericPropagationResponse>(
            normalizedEndpoint,
            JsonContent.Create(request),
            MaxGenericResponseBytes,
            cancellationToken);

        // 返回独立于 HTTP 响应缓冲的 Position，避免响应释放后不可读。
        return response with { Position = response.Position.Clone() };
    }

    private Task<TResponse> PostAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken)
        where TResponse : class, IAstroxSuccessResponse
        => SendAndReadAsync<TResponse>(
            endpoint,
            new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(request, options: RequestJsonOptions)
            },
            MaxTypedResponseBytes,
            cancellationToken);

    private Task<TResponse> PostContentAsync<TResponse>(
        string endpoint,
        HttpContent content,
        int maxResponseBytes,
        CancellationToken cancellationToken)
        where TResponse : class, IAstroxSuccessResponse
        => SendAndReadAsync<TResponse>(
            endpoint,
            new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            },
            maxResponseBytes,
            cancellationToken);

    private async Task<TResponse> SendAndReadAsync<TResponse>(
        string endpoint,
        HttpRequestMessage requestMessage,
        int maxResponseBytes,
        CancellationToken cancellationToken)
        where TResponse : class, IAstroxSuccessResponse
    {
        using (requestMessage)
        {
            HttpResponseMessage response;

            try
            {
                response = await _httpClient.SendAsync(
                    requestMessage,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new AstroxException($"Astrox call to {endpoint} failed: {ex.Message}", ex);
            }

            return await ReadSuccessResponseAsync<TResponse>(
                endpoint,
                response,
                maxResponseBytes,
                cancellationToken);
        }
    }

    private async Task<TResponse> ReadSuccessResponseAsync<TResponse>(
        string endpoint,
        HttpResponseMessage response,
        int maxResponseBytes,
        CancellationToken cancellationToken)
        where TResponse : class, IAstroxSuccessResponse
    {
        using (response)
        {
            try
            {
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                string serverMessage = await TryReadServerMessageAsync(
                        response,
                        maxResponseBytes,
                        cancellationToken)
                    ?? ex.Message;

                throw new AstroxException($"Astrox call to {endpoint} failed: {serverMessage}", ex);
            }

            try
            {
                string responseBody = await ReadBoundedBodyAsync(
                    endpoint,
                    response,
                    maxResponseBytes,
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(responseBody))
                {
                    throw new AstroxException($"Astrox call to {endpoint} returned an empty response body.");
                }

                using JsonDocument responseDocument = JsonDocument.Parse(responseBody);
                JsonElement root = responseDocument.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    ValidateCanonicalRootObject(root, endpoint);
                }

                if (IsSuccessfulPayload(root))
                {
                    ValidateSuccessfulPayload<TResponse>(root, endpoint);
                }

                TResponse? body = JsonSerializer.Deserialize<TResponse>(responseBody, ResponseJsonOptions);

                if (body is null)
                {
                    throw new AstroxException($"Astrox call to {endpoint} returned an empty response body.");
                }

                if (!body.IsSuccess)
                {
                    throw new AstroxException($"Astrox call to {endpoint} failed: {body.Message}");
                }

                return body;
            }
            catch (JsonException ex)
            {
                throw new AstroxException($"Astrox call to {endpoint} returned invalid JSON: {ex.Message}", ex);
            }
        }
    }

    private static async Task<string> ReadBoundedBodyAsync(
        string endpoint,
        HttpResponseMessage response,
        int maxResponseBytes,
        CancellationToken cancellationToken)
    {
        HttpContent content = response.Content;
        long? contentLength = content.Headers.ContentLength;
        if (contentLength is long length && length > maxResponseBytes)
        {
            throw new AstroxException(
                $"Astrox call to {endpoint} returned a response that is too large (Content-Length {contentLength}).");
        }

        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream(
            capacity: (int)Math.Min(contentLength ?? 8192, maxResponseBytes));
        byte[] chunk = new byte[8192];
        int total = 0;

        while (true)
        {
            int read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maxResponseBytes)
            {
                throw new AstroxException(
                    $"Astrox call to {endpoint} returned a response that is too large.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>
    /// 将 endpoint 规范化为仅允许的 /Propagator/* 相对路径，并阻断穿越与编码绕过。
    /// </summary>
    internal static string NormalizePropagatorEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Propagator endpoint cannot be blank.", nameof(endpoint));
        }

        string trimmed = endpoint.Trim();

        // 绝对 URL（含 scheme）与 protocol-relative authority。
        if (trimmed.Contains("://", StringComparison.Ordinal)
            || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Propagator endpoint cannot be an absolute URL or contain an authority.",
                nameof(endpoint));
        }

        if (trimmed[0] != '/'
            || trimmed.Contains('\\')
            || trimmed.Contains('?', StringComparison.Ordinal)
            || trimmed.Contains('#', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Propagator endpoint must be a root-relative /Propagator/* path without authority, query, or fragment.",
                nameof(endpoint));
        }

        ValidateDecodedPropagatorPath(trimmed, nameof(endpoint));

        if (!trimmed.StartsWith(PropagatorPrefix, StringComparison.Ordinal)
            || trimmed.Length <= PropagatorPrefix.Length)
        {
            throw new ArgumentException(
                "Propagator endpoint must start with /Propagator/ and name a propagator.",
                nameof(endpoint));
        }

        return trimmed;
    }

    private static void ValidateDecodedPropagatorPath(string path, string parameterName)
    {
        const int maximumDecodeRounds = 4;
        string current = path;

        for (int round = 0; round <= maximumDecodeRounds; round++)
        {
            if (current.Contains('\\', StringComparison.Ordinal)
                || current.StartsWith("//", StringComparison.Ordinal)
                || current.Contains('?', StringComparison.Ordinal)
                || current.Contains('#', StringComparison.Ordinal)
                || current.Split(['/', '\\']).Any(segment => segment is "." or ".."))
            {
                throw new ArgumentException(
                    "Propagator endpoint cannot contain backslashes, query/fragment, or dot segments.",
                    parameterName);
            }

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(current);
            }
            catch (UriFormatException ex)
            {
                throw new ArgumentException("Propagator endpoint contains invalid escaping.", parameterName, ex);
            }

            if (string.Equals(decoded, current, StringComparison.Ordinal))
            {
                return;
            }

            current = decoded;
        }

        throw new ArgumentException(
            "Propagator endpoint exceeds the supported URL-decoding depth.",
            parameterName);
    }

    private static bool IsSuccessfulPayload(JsonElement root)
        => root.ValueKind == JsonValueKind.Object
            && TryGetPropertyExact(root, "IsSuccess", out JsonElement isSuccess)
            && isSuccess.ValueKind is JsonValueKind.True;

    private static void ValidateCanonicalRootObject(JsonElement root, string endpoint)
    {
        EnsureNoCaseInsensitiveDuplicateNames(root, endpoint, "root");

        foreach (JsonProperty property in root.EnumerateObject())
        {
            foreach (string canonical in CanonicalRootKeys)
            {
                if (string.Equals(property.Name, canonical, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(property.Name, canonical, StringComparison.Ordinal))
                {
                    ThrowInvalidPayload(
                        endpoint,
                        $"Root key '{property.Name}' must use canonical casing '{canonical}'.");
                }
            }
        }
    }

    private static void ValidateSuccessfulPayload<TResponse>(
        JsonElement root,
        string endpoint)
        where TResponse : class, IAstroxSuccessResponse
    {
        if (typeof(TResponse) == typeof(SsoResponse))
        {
            ValidateSsoElements(root, endpoint);
            return;
        }

        if (typeof(TResponse) == typeof(J2Response)
            || typeof(TResponse) == typeof(GenericPropagationResponse))
        {
            ValidateJ2Position(root, endpoint);
        }
    }

    private static void ValidateSsoElements(JsonElement root, string endpoint)
    {
        if (!TryGetPropertyExact(root, "Elements_Inertial", out JsonElement elements)
            || elements.ValueKind != JsonValueKind.Object)
        {
            ThrowInvalidPayload(endpoint, "Elements_Inertial must be an object.");
        }

        EnsureNoCaseInsensitiveDuplicateNames(elements, endpoint, "Elements_Inertial");

        string[] requiredProperties =
        [
            "SemimajorAxis",
            "Eccentricity",
            "Inclination",
            "ArgumentOfPeriapsis",
            "RightAscensionOfAscendingNode",
            "TrueAnomaly",
            "GravitationalParameter"
        ];
        foreach (string propertyName in requiredProperties)
        {
            if (!TryGetPropertyExact(elements, propertyName, out JsonElement value)
                || !IsFiniteNumber(value))
            {
                ThrowInvalidPayload(
                    endpoint,
                    $"Elements_Inertial.{propertyName} must be a finite number.");
            }
        }
    }

    private static void ValidateJ2Position(JsonElement root, string endpoint)
    {
        if (!TryGetPropertyExact(root, "Position", out JsonElement position)
            || position.ValueKind != JsonValueKind.Object)
        {
            ThrowInvalidPayload(endpoint, "Position must be an object.");
        }

        EnsureNoCaseInsensitiveDuplicateNames(position, endpoint, "Position");

        if (!TryGetPropertyExact(position, "epoch", out JsonElement epoch)
            || epoch.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(epoch.GetString())
            || !DateTimeOffset.TryParse(
                epoch.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out _))
        {
            ThrowInvalidPayload(endpoint, "Position.epoch must be a valid timestamp.");
        }

        bool hasCartesian = TryGetPropertyExact(
            position,
            "cartesian",
            out JsonElement cartesian);
        bool hasCartesianVelocity = TryGetPropertyExact(
            position,
            "cartesianVelocity",
            out JsonElement cartesianVelocity);
        if (!hasCartesian && !hasCartesianVelocity)
        {
            ThrowInvalidPayload(
                endpoint,
                "Position must contain cartesian or cartesianVelocity samples.");
        }

        if (hasCartesian)
        {
            ValidatePositionSamples(endpoint, "cartesian", cartesian, stride: 4);
        }

        if (hasCartesianVelocity)
        {
            ValidatePositionSamples(
                endpoint,
                "cartesianVelocity",
                cartesianVelocity,
                stride: 7);
        }
    }

    private static void ValidatePositionSamples(
        string endpoint,
        string propertyName,
        JsonElement samples,
        int stride)
    {
        if (samples.ValueKind != JsonValueKind.Array
            || samples.GetArrayLength() == 0
            || samples.GetArrayLength() % stride != 0
            || samples.EnumerateArray().Any(value => !IsFiniteNumber(value)))
        {
            ThrowInvalidPayload(
                endpoint,
                $"Position.{propertyName} must be a non-empty finite numeric array with stride {stride}.");
        }
    }

    private static bool IsFiniteNumber(JsonElement value)
        => value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out double number)
            && double.IsFinite(number);

    private static void EnsureNoCaseInsensitiveDuplicateNames(
        JsonElement obj,
        string endpoint,
        string context)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in obj.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                ThrowInvalidPayload(
                    endpoint,
                    $"{context} cannot contain duplicate keys (including case variants): '{property.Name}'.");
            }
        }
    }

    private static bool TryGetPropertyExact(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.Ordinal))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static void ThrowInvalidPayload(string endpoint, string detail)
        => throw new AstroxException(
            $"Astrox call to {endpoint} returned an invalid success payload: {detail}");

    private static JsonSerializerOptions CreateRequestOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null
        };

        options.Converters.Add(new UtcMillisecondDateTimeOffsetConverter());
        return options;
    }

    private static JsonSerializerOptions CreateResponseOptions()
    {
        // 大小写敏感，避免“验证首个、消费最后一个”的大小写变体绕过。
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false
        };

        options.Converters.Add(new UtcMillisecondDateTimeOffsetConverter());
        return options;
    }

    private static async Task<string?> TryReadServerMessageAsync(
        HttpResponseMessage response,
        int maxResponseBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            string responseBody = await ReadBoundedBodyAsync(
                "error",
                response,
                maxResponseBytes,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            AstroxErrorBody? error = JsonSerializer.Deserialize<AstroxErrorBody>(responseBody, ResponseJsonOptions);
            return string.IsNullOrWhiteSpace(error?.Message) ? null : error.Message;
        }
        catch (AstroxException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record AstroxErrorBody(string? Message);
}
