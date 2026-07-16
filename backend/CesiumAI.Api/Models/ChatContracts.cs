using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace CesiumAI.Api.Models;

public sealed record ChatRequest(
    [property: Required, MinLength(1)] string Message,
    string? SessionId,
    [property: Required] SceneSummary SceneSummary,
    IReadOnlyList<JsonElement>? RelevantPackets);

public sealed record ChatResponse(
    string SessionId,
    string Message,
    IReadOnlyList<SceneOp> SceneOps);

public sealed record SceneSummary(
    DocumentClockSummary? DocumentClock,
    IReadOnlyList<EntitySummary> Entities);

public sealed record DocumentClockSummary(string? Interval, string? CurrentTime);

public sealed record EntitySummary(
    string Id,
    string? Name,
    string Type,
    double? Lon,
    double? Lat,
    double? Alt,
    string? OrbitHint);
