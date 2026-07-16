using System.Text.Json;
using System.Text.Json.Serialization;

namespace CesiumAI.Api.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(ClearSceneOp), "clear")]
[JsonDerivedType(typeof(UpsertSceneOp), "upsert")]
[JsonDerivedType(typeof(DeleteSceneOp), "delete")]
public abstract record SceneOp;

public sealed record ClearSceneOp : SceneOp;

public sealed record UpsertSceneOp(
    [property: JsonPropertyName("packets")] IReadOnlyList<JsonElement> Packets) : SceneOp;

public sealed record DeleteSceneOp(
    [property: JsonPropertyName("ids")] IReadOnlyList<string> Ids) : SceneOp;
