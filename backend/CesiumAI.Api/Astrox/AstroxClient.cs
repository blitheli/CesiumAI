using System.Net.Http.Json;
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
            TResponse? body = await response.Content.ReadFromJsonAsync<TResponse>(ResponseJsonOptions, cancellationToken);

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
            AstroxErrorBody? error = await response.Content.ReadFromJsonAsync<AstroxErrorBody>(ResponseJsonOptions, cancellationToken);
            return string.IsNullOrWhiteSpace(error?.Message) ? null : error.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record AstroxErrorBody(string? Message);
}
