using System.Text;
using System.Text.Json;
using CesiumAI.Api.Services;
using FluentAssertions;

namespace CesiumAI.Api.Tests.Services;

public class SceneStyleValidatorTests
{
    private readonly ISceneStyleValidator _validator = new SceneStyleValidator();

    [Fact]
    public void ValidateAndClone_AcceptsAllowedVisualProperties()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "point": { "color": { "rgba": [255, 0, 0, 255] } },
              "path": { "width": 5 }
            }
            """);

        JsonElement clone = _validator.ValidateAndClone(document.RootElement);

        clone.GetProperty("point").GetProperty("color").GetProperty("rgba")
            .EnumerateArray().Select(value => value.GetInt32()).Should().Equal([255, 0, 0, 255]);
        clone.GetProperty("path").GetProperty("width").GetInt32().Should().Be(5);
    }

    [Fact]
    public void ValidateAndClone_AcceptsNullDeletionForAllowedProperties()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "label": null,
              "path": { "width": null }
            }
            """);

        JsonElement clone = _validator.ValidateAndClone(document.RootElement);

        clone.GetProperty("label").ValueKind.Should().Be(JsonValueKind.Null);
        clone.GetProperty("path").GetProperty("width").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("position")]
    [InlineData("availability")]
    [InlineData("properties")]
    [InlineData("unknown")]
    public void ValidateAndClone_RejectsForbiddenOrUnknownTopLevelKeys(string key)
    {
        using JsonDocument document = JsonDocument.Parse($$"""{ "{{key}}": {} }""");

        Action act = () => _validator.ValidateAndClone(document.RootElement);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("[255,0,0]")]
    [InlineData("[255,0,0,255,1]")]
    [InlineData("[256,0,0,255]")]
    [InlineData("[-1,0,0,255]")]
    [InlineData("[255.5,0,0,255]")]
    [InlineData("[0.0000001,0,0,255]")]
    [InlineData("[1.1,2,3,4]")]
    [InlineData("[1e400,0,0,255]")]
    public void ValidateAndClone_RejectsInvalidRgbaArrays(string rgbaJson)
    {
        using JsonDocument document = JsonDocument.Parse($$"""{ "point": { "color": { "rgba": {{rgbaJson}} } } }""");

        Action act = () => _validator.ValidateAndClone(document.RootElement);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void JsonElement_TryGetInt32_RejectsExactIntegerWrittenAsJsonFraction()
    {
        // 证据：System.Text.Json 对 JSON `1.0` 的 TryGetInt32 返回 false，
        // 而前端 Number.isInteger(JSON.parse('1.0')) 为 true。不能再用 TryGetInt32 作为“精确整数”语义。
        using JsonDocument document = JsonDocument.Parse("""{"v":1.0}""");
        JsonElement value = document.RootElement.GetProperty("v");

        value.TryGetInt32(out _).Should().BeFalse();
        value.TryGetDouble(out double number).Should().BeTrue();
        number.Should().Be(1.0);
        double.IsInteger(number).Should().BeTrue();
    }

    [Fact]
    public void ValidateAndClone_AcceptsRgbaExactIntegersWrittenAsJsonFractions()
    {
        using JsonDocument document = JsonDocument.Parse("""
            { "point": { "color": { "rgba": [1.0, 2.0, 3.0, 255.0] } } }
            """);

        JsonElement clone = _validator.ValidateAndClone(document.RootElement);

        clone.GetProperty("point").GetProperty("color").GetProperty("rgba")
            .EnumerateArray().Select(value => value.GetDouble()).Should().Equal([1.0, 2.0, 3.0, 255.0]);
    }

    [Theory]
    [InlineData("""{ "path": { "width": -1 } }""")]
    [InlineData("""{ "point": { "pixelSize": -0.5 } }""")]
    [InlineData("""{ "point": { "outlineWidth": -2 } }""")]
    [InlineData("""{ "billboard": { "scale": -0.01 } }""")]
    public void ValidateAndClone_RejectsNegativeNumericVisualValues(string patchJson)
    {
        using JsonDocument document = JsonDocument.Parse(patchJson);

        Action act = () => _validator.ValidateAndClone(document.RootElement);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateAndClone_RejectsOverflowingJsonNumber_WhenTryGetDoubleFails()
    {
        // 标准 JSON Number 溢出（如 1e400）：ValueKind 仍为 Number，但 TryGetDouble 返回 false。
        using JsonDocument document = JsonDocument.Parse("""{"path":{"width":1e400}}""");

        Action act = () => _validator.ValidateAndClone(document.RootElement);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateAndClone_RejectsPayloadLargerThan32KiB()
    {
        string largeText = new string('a', 33 * 1024);
        using JsonDocument document = JsonDocument.Parse($$"""{ "label": { "text": "{{largeText}}" } }""");

        Action act = () => _validator.ValidateAndClone(document.RootElement);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateAndClone_RejectsNestingDeeperThan12()
    {
        var builder = new StringBuilder();
        builder.Append("""{ "point": """);
        for (int i = 0; i < 13; i++)
        {
            builder.Append("""{ "nested": """);
        }

        builder.Append("1");
        for (int i = 0; i < 13; i++)
        {
            builder.Append('}');
        }

        builder.Append('}');

        using JsonDocument document = JsonDocument.Parse(builder.ToString());

        Action act = () => _validator.ValidateAndClone(document.RootElement);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateAndClone_RejectsArrayLongerThan4096()
    {
        var values = Enumerable.Repeat(1, 4097);
        string arrayJson = string.Join(',', values);
        using JsonDocument document = JsonDocument.Parse($$"""{ "polyline": { "positions": { "cartesian": [{{arrayJson}}] } } }""");

        Action act = () => _validator.ValidateAndClone(document.RootElement);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateAndClone_ReturnsIndependentClone()
    {
        JsonElement clone;
        using (JsonDocument document = JsonDocument.Parse("""{ "path": { "width": 5 } }"""))
        {
            clone = _validator.ValidateAndClone(document.RootElement);
        }

        // 原 JsonDocument 已释放后，clone 仍可安全读取。
        clone.GetProperty("path").GetProperty("width").GetInt32().Should().Be(5);
    }

    [Fact]
    public void ValidateAndClone_RejectsNonObjectRoot()
    {
        using JsonDocument document = JsonDocument.Parse("[1,2,3]");

        Action act = () => _validator.ValidateAndClone(document.RootElement);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("""{ "billboard": { "image": "https://evil.example/a.png" } }""")]
    [InlineData("""{ "billboard": { "uri": "/local/icon.png" } }""")]
    [InlineData("""{ "billboard": { "url": "data:image/png;base64,xx" } }""")]
    [InlineData("""{ "model": { "gltf": "https://evil.example/m.gltf" } }""")]
    [InlineData("""{ "model": { "uri": "models/sat.glb" } }""")]
    [InlineData("""{ "model": { "url": "https://evil.example/m.glb" } }""")]
    [InlineData("""{ "billboard": { "scale": 2, "nested": { "image": "x.png" } } }""")]
    [InlineData("""{ "model": { "scale": 1, "nodeTransformations": { "a": { "uri": "y" } } } }""")]
    public void ValidateAndClone_RejectsExternalResourceKeysInBillboardOrModel(string patchJson)
    {
        using JsonDocument document = JsonDocument.Parse(patchJson);

        Action act = () => _validator.ValidateAndClone(document.RootElement);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateAndClone_AllowsNullExternalResourceKeys_AndOtherVisualFields()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "billboard": { "image": null, "scale": 2 },
              "model": { "gltf": null, "uri": null, "url": null, "scale": 1 },
              "path": { "width": 5 }
            }
            """);

        JsonElement clone = _validator.ValidateAndClone(document.RootElement);

        clone.GetProperty("billboard").GetProperty("scale").GetDouble().Should().Be(2);
        clone.GetProperty("path").GetProperty("width").GetInt32().Should().Be(5);
    }
}
