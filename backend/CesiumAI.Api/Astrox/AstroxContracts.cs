using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CesiumAI.Api.Astrox;

public interface IAstroxClient
{
    Task<SsoResponse> CreateSsoAsync(SsoRequest request, CancellationToken cancellationToken);

    Task<J2Response> PropagateJ2Async(J2Request request, CancellationToken cancellationToken);
}

public interface IOrbitScenarioService
{
    Task<JsonElement?> CreateSsoJ2PacketAsync(SsoJ2Scenario scenario, CancellationToken cancellationToken);
}

public sealed record SsoRequest(
    string Description,
    [property: JsonConverter(typeof(UtcMillisecondDateTimeOffsetConverter))] DateTimeOffset OrbitEpoch,
    double Altitude,
    double LocalTimeOfDescendingNode);

public sealed record SsoResponse(
    bool IsSuccess,
    string Message,
    [property: JsonPropertyName("Elements_Inertial")] OrbitalElements ElementsInertial) : IAstroxSuccessResponse;

public sealed record OrbitalElements(
    double SemimajorAxis,
    double Eccentricity,
    double Inclination,
    double ArgumentOfPeriapsis,
    double RightAscensionOfAscendingNode,
    double TrueAnomaly,
    double GravitationalParameter);

public sealed record J2Request(
    [property: JsonConverter(typeof(UtcMillisecondDateTimeOffsetConverter))] DateTimeOffset Start,
    [property: JsonConverter(typeof(UtcMillisecondDateTimeOffsetConverter))] DateTimeOffset Stop,
    string CentralBody,
    [property: JsonConverter(typeof(UtcMillisecondDateTimeOffsetConverter))] DateTimeOffset OrbitEpoch,
    string CoordType,
    IReadOnlyList<double> OrbitalElements,
    int Step);

public sealed record J2Response(
    bool IsSuccess,
    string Message,
    JsonElement Position,
    double Period) : IAstroxSuccessResponse;

public sealed record SsoJ2Scenario(
    string Id,
    string Name,
    double AltitudeKm,
    DateTimeOffset EpochUtc,
    double Hours,
    int StepSeconds,
    double LocalTimeOfDescendingNode);

public sealed class AstroxException : Exception
{
    public AstroxException(string message)
        : base(message)
    {
    }

    public AstroxException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal interface IAstroxSuccessResponse
{
    bool IsSuccess { get; }

    string Message { get; }
}

internal sealed class UtcMillisecondDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateTimeOffset.Parse(
            reader.GetString() ?? throw new JsonException("Expected a UTC timestamp string."),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
}
