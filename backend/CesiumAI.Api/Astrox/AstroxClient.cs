using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using CesiumAI.Api.Configuration;
using Microsoft.Extensions.Options;

namespace CesiumAI.Api.Astrox;

public sealed class AstroxClient : IAstroxClient
{
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

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest request, CancellationToken cancellationToken)
        where TResponse : class, IAstroxSuccessResponse
    {
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.PostAsJsonAsync(endpoint, request, RequestJsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AstroxException($"Astrox call to {endpoint} failed: {ex.Message}", ex);
        }

        using (response)
        {
            try
            {
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                string serverMessage = await TryReadServerMessageAsync(response, cancellationToken)
                    ?? ex.Message;

                throw new AstroxException($"Astrox call to {endpoint} failed: {serverMessage}", ex);
            }

            try
            {
                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (string.IsNullOrWhiteSpace(responseBody))
                {
                    throw new AstroxException($"Astrox call to {endpoint} returned an empty response body.");
                }

                using JsonDocument responseDocument = JsonDocument.Parse(responseBody);
                JsonElement root = responseDocument.RootElement;
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

    private static bool IsSuccessfulPayload(JsonElement root)
        => root.ValueKind == JsonValueKind.Object
            && TryGetProperty(root, "IsSuccess", out JsonElement isSuccess)
            && isSuccess.ValueKind is JsonValueKind.True;

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

        if (typeof(TResponse) == typeof(J2Response))
        {
            ValidateJ2Position(root, endpoint);
        }
    }

    private static void ValidateSsoElements(JsonElement root, string endpoint)
    {
        if (!TryGetProperty(root, "Elements_Inertial", out JsonElement elements)
            || elements.ValueKind != JsonValueKind.Object)
        {
            ThrowInvalidPayload(endpoint, "Elements_Inertial must be an object.");
        }

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
            if (!TryGetProperty(elements, propertyName, out JsonElement value)
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
        if (!TryGetProperty(root, "Position", out JsonElement position)
            || position.ValueKind != JsonValueKind.Object)
        {
            ThrowInvalidPayload(endpoint, "Position must be an object.");
        }

        if (!TryGetProperty(position, "epoch", out JsonElement epoch)
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

        bool hasCartesian = TryGetProperty(
            position,
            "cartesian",
            out JsonElement cartesian);
        bool hasCartesianVelocity = TryGetProperty(
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

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(
                property.Name,
                propertyName,
                StringComparison.OrdinalIgnoreCase))
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
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        options.Converters.Add(new UtcMillisecondDateTimeOffsetConverter());
        return options;
    }

    private static async Task<string?> TryReadServerMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            AstroxErrorBody? error = JsonSerializer.Deserialize<AstroxErrorBody>(responseBody, ResponseJsonOptions);
            return string.IsNullOrWhiteSpace(error?.Message) ? null : error.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record AstroxErrorBody(string? Message);
}
