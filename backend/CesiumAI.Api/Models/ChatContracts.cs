using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CesiumAI.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter<EntityType>))]
public enum EntityType
{
    [JsonStringEnumMemberName("facility")]
    Facility,

    [JsonStringEnumMemberName("satellite")]
    Satellite,

    [JsonStringEnumMemberName("other")]
    Other
}

public sealed record ChatRequest(
    [property: JsonPropertyName("message")]
    [property: Required, MinLength(1)] string Message,
    [property: JsonPropertyName("sessionId")] string? SessionId,
    [property: JsonPropertyName("sceneSummary")]
    [property: Required] SceneSummary SceneSummary,
    [property: JsonPropertyName("relevantPackets")]
    IReadOnlyList<JsonElement>? RelevantPackets);

public sealed record ChatResponse(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("sceneOps")] IReadOnlyList<SceneOp> SceneOps);

public sealed record SceneSummary(
    [property: JsonPropertyName("documentClock")]
    DocumentClockSummary? DocumentClock,
    [property: JsonPropertyName("entities")]
    IReadOnlyList<EntitySummary> Entities);

public sealed record DocumentClockSummary(
    [property: JsonPropertyName("interval")] string? Interval,
    [property: JsonPropertyName("currentTime")] string? CurrentTime);

public sealed record EntitySummary(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("type")] EntityType Type,
    [property: JsonPropertyName("lon")] double? Lon,
    [property: JsonPropertyName("lat")] double? Lat,
    [property: JsonPropertyName("alt")] double? Alt,
    [property: JsonPropertyName("orbitHint")] string? OrbitHint);
