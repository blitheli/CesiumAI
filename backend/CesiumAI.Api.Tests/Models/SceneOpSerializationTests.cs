using System.Text.Json;
using CesiumAI.Api.Models;
using FluentAssertions;

namespace CesiumAI.Api.Tests.Models;

public class SceneOpSerializationTests
{
    private static readonly SceneOp[] SampleOperations =
    [
        new ClearSceneOp(),
        new UpsertSceneOp([JsonSerializer.SerializeToElement(new { id = "sanya" })]),
        new DeleteSceneOp(["obsolete"])
    ];

    [Fact]
    public void SceneOps_SerializeWithOpDiscriminatorAndPayloadFields()
    {
        string json = JsonSerializer.Serialize(
            SampleOperations,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("\"op\":\"clear\"");
        json.Should().Contain("\"op\":\"upsert\"");
        json.Should().Contain("\"op\":\"delete\"");
        json.Should().Contain("\"packets\"");
        json.Should().Contain("\"ids\"");
    }

    [Fact]
    public void SceneOps_DefaultSerializer_UsesCamelCaseWireNames()
    {
        string json = JsonSerializer.Serialize(SampleOperations);

        json.Should().Contain("\"op\":\"clear\"");
        json.Should().Contain("\"op\":\"upsert\"");
        json.Should().Contain("\"op\":\"delete\"");
        json.Should().Contain("\"packets\"");
        json.Should().Contain("\"ids\"");
        json.Should().NotContain("\"Packets\"");
        json.Should().NotContain("\"Ids\"");
    }

    [Fact]
    public void ChatRequest_DefaultSerializer_UsesCamelCaseWireNames()
    {
        var request = new ChatRequest(
            Message: "show sanya",
            SessionId: "s1",
            SceneSummary: new SceneSummary(
                DocumentClock: new DocumentClockSummary("2026-01-01/2026-12-31", "2026-07-16T00:00:00Z"),
                Entities:
                [
                    new EntitySummary("sanya", "Sanya", EntityType.Facility, 109.5, 18.2, 0, null)
                ]),
            RelevantPackets: [JsonSerializer.SerializeToElement(new { id = "sanya" })]);

        string json = JsonSerializer.Serialize(request);

        json.Should().Contain("\"message\"");
        json.Should().Contain("\"sessionId\"");
        json.Should().Contain("\"sceneSummary\"");
        json.Should().Contain("\"relevantPackets\"");
        json.Should().Contain("\"documentClock\"");
        json.Should().Contain("\"entities\"");
        json.Should().Contain("\"interval\"");
        json.Should().Contain("\"currentTime\"");
        json.Should().Contain("\"orbitHint\"");
        json.Should().Contain("\"type\":\"facility\"");
        json.Should().NotContain("\"Message\"");
        json.Should().NotContain("\"SessionId\"");
        json.Should().NotContain("\"SceneSummary\"");
    }

    [Fact]
    public void ChatResponse_DefaultSerializer_UsesCamelCaseWireNames()
    {
        var response = new ChatResponse(
            SessionId: "s1",
            Message: "done",
            SceneOps: SampleOperations);

        string json = JsonSerializer.Serialize(response);

        json.Should().Contain("\"sessionId\"");
        json.Should().Contain("\"message\"");
        json.Should().Contain("\"sceneOps\"");
        json.Should().Contain("\"op\":\"clear\"");
        json.Should().NotContain("\"SessionId\"");
        json.Should().NotContain("\"Message\"");
        json.Should().NotContain("\"SceneOps\"");
    }
}
