using System.Text.Json;
using CesiumAI.Api.Models;
using FluentAssertions;

namespace CesiumAI.Api.Tests.Models;

public class SceneOpSerializationTests
{
    [Fact]
    public void SceneOps_SerializeWithOpDiscriminatorAndPayloadFields()
    {
        SceneOp[] operations =
        [
            new ClearSceneOp(),
            new UpsertSceneOp([JsonSerializer.SerializeToElement(new { id = "sanya" })]),
            new DeleteSceneOp(["obsolete"])
        ];

        string json = JsonSerializer.Serialize(
            operations,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("\"op\":\"clear\"");
        json.Should().Contain("\"op\":\"upsert\"");
        json.Should().Contain("\"op\":\"delete\"");
        json.Should().Contain("\"packets\"");
        json.Should().Contain("\"ids\"");
    }
}
