using System.Text.Json;
using CesiumAI.Api.Models;
using CesiumAI.Api.Services;
using FluentAssertions;

namespace CesiumAI.Api.Tests.Services;

public class ScenePromptBuilderTests
{
    [Fact]
    public void Build_OrdersSceneSummaryRelevantPacketsAndUserMessage()
    {
        var packet = JsonSerializer.SerializeToElement(new
        {
            id = "sanya",
            position = new { cartographicDegrees = new[] { 109.5, 18.2, 0.0 } }
        });
        var request = new ChatRequest(
            "把 sanya 高度改为 50 米",
            "session-1",
            new SceneSummary(
                new DocumentClockSummary("2026-01-01/2026-01-02", "2026-01-01"),
                [new EntitySummary("sanya", "三亚", EntityType.Facility, 109.5, 18.2, 0, null)]),
            [packet]);

        string prompt = new ScenePromptBuilder().Build(request);

        prompt.Should().Be(
            """
            [SCENE_SUMMARY]
            {"documentClock":{"interval":"2026-01-01/2026-01-02","currentTime":"2026-01-01"},"entities":[{"id":"sanya","name":"\u4E09\u4E9A","type":"facility","lon":109.5,"lat":18.2,"alt":0,"orbitHint":null}]}

            [RELEVANT_CZML_PACKETS]
            [{"id":"sanya","position":{"cartographicDegrees":[109.5,18.2,0]}}]

            [USER]
            把 sanya 高度改为 50 米
            """);
    }

    [Fact]
    public void Build_UsesEmptyArrayWhenRelevantPacketsAreAbsent()
    {
        var request = new ChatRequest(
            "当前场景里有什么？",
            null,
            new SceneSummary(null, []),
            null);

        string prompt = new ScenePromptBuilder().Build(request);

        prompt.Should().Contain(
            """
            [RELEVANT_CZML_PACKETS]
            []
            """);
    }
}
