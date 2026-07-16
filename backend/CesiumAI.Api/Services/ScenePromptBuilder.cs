using System.Text.Json;
using CesiumAI.Api.Models;

namespace CesiumAI.Api.Services;

public interface IScenePromptBuilder
{
    string Build(ChatRequest request);
}

public sealed class ScenePromptBuilder : IScenePromptBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public string Build(ChatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string summary = JsonSerializer.Serialize(request.SceneSummary, JsonOptions);
        string packets = JsonSerializer.Serialize(request.RelevantPackets ?? [], JsonOptions);

        return $"[SCENE_SUMMARY]\n{summary}\n\n"
            + $"[RELEVANT_CZML_PACKETS]\n{packets}\n\n"
            + $"[USER]\n{request.Message}";
    }
}
