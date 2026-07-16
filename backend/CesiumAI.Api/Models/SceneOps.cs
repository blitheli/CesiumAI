using System.Text.Json;
using System.Text.Json.Serialization;

namespace CesiumAI.Api.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(ClearSceneOp), "clear")]
[JsonDerivedType(typeof(UpsertSceneOp), "upsert")]
[JsonDerivedType(typeof(DeleteSceneOp), "delete")]
[JsonDerivedType(typeof(CameraSceneOp), "camera")]
[JsonDerivedType(typeof(StyleSceneOp), "style")]
public abstract record SceneOp;

public sealed record ClearSceneOp : SceneOp;

public sealed record UpsertSceneOp(
    [property: JsonPropertyName("packets")] IReadOnlyList<JsonElement> Packets) : SceneOp;

public sealed record DeleteSceneOp(
    [property: JsonPropertyName("ids")] IReadOnlyList<string> Ids) : SceneOp;

public sealed record CameraSceneOp(
    [property: JsonPropertyName("action")] CameraAction Action,
    [property: JsonPropertyName("targetId")] string? TargetId = null,
    [property: JsonPropertyName("distanceMeters")] double? DistanceMeters = null,
    [property: JsonPropertyName("headingDegrees")] double? HeadingDegrees = null,
    [property: JsonPropertyName("pitchDegrees")] double? PitchDegrees = null,
    [property: JsonPropertyName("rollDegrees")] double? RollDegrees = null,
    [property: JsonPropertyName("amount")] double? Amount = null,
    [property: JsonPropertyName("direction")] string? Direction = null,
    [property: JsonPropertyName("angularSpeedDegreesPerSecond")] double? AngularSpeedDegreesPerSecond = null) : SceneOp;

public sealed record StyleSceneOp(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("patch")] JsonElement Patch) : SceneOp;
