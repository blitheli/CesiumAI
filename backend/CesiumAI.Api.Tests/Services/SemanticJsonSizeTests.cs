using System.Text;
using System.Text.Json;
using CesiumAI.Api.Services;
using FluentAssertions;

namespace CesiumAI.Api.Tests.Services;

public class SemanticJsonSizeTests
{
    [Fact]
    public void Measure_MatchesLiteralObjectArrayAndPrimitiveBudgets()
    {
        using JsonDocument empty = JsonDocument.Parse("{}");
        SemanticJsonSize.Measure(empty.RootElement).Should().Be(2);

        using JsonDocument nullLabel = JsonDocument.Parse("""{"label":null}""");
        // {"label":null} => braces 2 + "label" 7 + colon 1 + null 4 = 14
        SemanticJsonSize.Measure(nullLabel.RootElement).Should().Be(14);

        using JsonDocument pathWidth = JsonDocument.Parse("""{"path":{"width":5}}""");
        // outer: 2 + "path"(6) + 1 + inner
        // inner: 2 + "width"(7) + 1 + number(24) = 34
        // total: 2+6+1+34 = 43
        SemanticJsonSize.Measure(pathWidth.RootElement).Should().Be(43);

        using JsonDocument arr = JsonDocument.Parse("[true,false,null]");
        // brackets 2 + true 4 + comma + false 5 + comma + null 4 = 17
        SemanticJsonSize.Measure(arr.RootElement).Should().Be(17);

        using JsonDocument text = JsonDocument.Parse("""{"label":{"text":"a\"b"}}""");
        // "a\"b" JSON UTF-8 length is 6
        int textSize = SemanticJsonSize.Measure(text.RootElement);
        int expected =
            2 + 7 + 1 // {"label":
            + (2 + 6 + 1 + 6) // {"text":"a\"b"}
            ;
        textSize.Should().Be(expected);
    }

    [Fact]
    public void Measure_CountsEveryNumberAsFixed24Bytes_RegardlessOfRawDigits()
    {
        using JsonDocument compact = JsonDocument.Parse("1");
        using JsonDocument scientific = JsonDocument.Parse("1e20");
        SemanticJsonSize.Measure(compact.RootElement).Should().Be(SemanticJsonSize.NumberBudgetBytes);
        SemanticJsonSize.Measure(scientific.RootElement).Should().Be(SemanticJsonSize.NumberBudgetBytes);
        SemanticJsonSize.NumberBudgetBytes.Should().Be(24);
        SemanticJsonSize.MaxSemanticBytes.Should().Be(32 * 1024);
    }

    [Theory]
    [InlineData("\"你好\"", 8)]
    [InlineData("\"<\"", 3)]
    [InlineData("\"a\\\"b\\\\c\"", 9)]
    [InlineData("\"\\u0000\\u0001\\n\\t\"", 18)]
    [InlineData("\"\\u2028\"", 5)]
    [InlineData("\"\\u2029\"", 5)]
    [InlineData("\"a<\\u2028\\u2029>你好\"", 17)]
    public void Measure_StringMatchesBrowserJsonStringifyUtf8ByteCount(string jsonStringLiteral, int expectedBytes)
    {
        using JsonDocument document = JsonDocument.Parse(jsonStringLiteral);
        SemanticJsonSize.Measure(document.RootElement).Should().Be(expectedBytes);
    }

    [Fact]
    public void Measure_TreatsRawAndUnicodeEscapeFormsAsSameSemanticString()
    {
        using JsonDocument raw = JsonDocument.Parse("\"你好\"");
        using JsonDocument escaped = JsonDocument.Parse("\"\\u4f60\\u597d\"");
        using JsonDocument lineSepRaw = JsonDocument.Parse("\"\u2028\"");
        using JsonDocument lineSepEscaped = JsonDocument.Parse("\"\\u2028\"");

        SemanticJsonSize.Measure(raw.RootElement).Should().Be(8);
        SemanticJsonSize.Measure(escaped.RootElement).Should().Be(8);
        SemanticJsonSize.Measure(lineSepRaw.RootElement).Should().Be(5);
        SemanticJsonSize.Measure(lineSepEscaped.RootElement).Should().Be(5);
    }

    [Fact]
    public void Measure_DoesNotConfuseLiteralBackslashUSequenceWithLineSeparator()
    {
        // 语义字符串为六个字符：\ u 2 0 2 8 —— JSON.stringify => "\\u2028"（9 bytes）
        using JsonDocument document = JsonDocument.Parse("\"\\\\u2028\"");
        SemanticJsonSize.Measure(document.RootElement).Should().Be(9);
    }

    [Fact]
    public void Measure_ObjectKeyUsesSameBrowserAlignedStringEncoding()
    {
        using JsonDocument document = JsonDocument.Parse("{\"a<\\u2028\":null}");
        // key JSON.stringify("a<\u2028") = "a<\u2028" => 2+1+1+3 = 7 UTF-8 bytes?
        // "a<" = 3 chars ASCII in quotes... full: quote + a + < + U+2028(3) + quote = 7
        // object: 2 + 7 + 1 + 4 = 14
        SemanticJsonSize.Measure(document.RootElement).Should().Be(14);
    }
}

public class SceneStyleSemanticBudgetTests
{
    private readonly ISceneStyleValidator _validator = new SceneStyleValidator();

    [Fact]
    public void ValidateAndClone_Rejects4096ScientificNumbersBySemanticBudget()
    {
        // raw `1e20` 很短，规范化/默认序列化可能膨胀；语义预算对每个 number 固定计 24。
        // 4096 * 24 + 数组括号/逗号 + 包装对象 >> 32KiB，前后端必须同拒。
        var values = Enumerable.Repeat("1e20", 4096);
        string arrayJson = string.Join(',', values);
        string patchJson =
            """{"polyline":{"positions":{"cartesian":[""" + arrayJson + "]}}}";

        Encoding.UTF8.GetByteCount(patchJson).Should().BeLessThan(SemanticJsonSize.MaxSemanticBytes);

        using JsonDocument document = JsonDocument.Parse(patchJson);
        SemanticJsonSize.Measure(document.RootElement).Should().BeGreaterThan(SemanticJsonSize.MaxSemanticBytes);

        Action act = () => _validator.ValidateAndClone(document.RootElement);
        act.Should().Throw<ArgumentException>().WithMessage("*语义*");
    }

    [Fact]
    public void ValidateAndClone_AcceptsSmallBoundaryObjectUnderSemanticBudget()
    {
        using JsonDocument document = JsonDocument.Parse("""{"path":{"width":5},"point":{"pixelSize":1}}""");

        SemanticJsonSize.Measure(document.RootElement).Should().BeLessThan(SemanticJsonSize.MaxSemanticBytes);

        JsonElement clone = _validator.ValidateAndClone(document.RootElement);
        clone.GetProperty("path").GetProperty("width").GetInt32().Should().Be(5);
        clone.GetProperty("point").GetProperty("pixelSize").GetInt32().Should().Be(1);
    }

    [Fact]
    public void ValidateAndClone_AcceptsPatchAtExactSemanticBudgetLimit()
    {
        // {"label":{"text":<string>}} 语义开销 21；N 个 ASCII 使总数恰为 32768。
        int textLength = SemanticJsonSize.MaxSemanticBytes - 21;
        string text = new string('x', textLength);
        string patchJson = "{\"label\":{\"text\":\"" + text + "\"}}";

        using JsonDocument document = JsonDocument.Parse(patchJson);
        SemanticJsonSize.Measure(document.RootElement).Should().Be(SemanticJsonSize.MaxSemanticBytes);

        Action act = () => _validator.ValidateAndClone(document.RootElement);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateAndClone_RejectsPatchOneByteOverSemanticBudgetLimit()
    {
        int textLength = SemanticJsonSize.MaxSemanticBytes - 21 + 1;
        string text = new string('x', textLength);
        string patchJson = "{\"label\":{\"text\":\"" + text + "\"}}";

        using JsonDocument document = JsonDocument.Parse(patchJson);
        SemanticJsonSize.Measure(document.RootElement).Should().Be(SemanticJsonSize.MaxSemanticBytes + 1);

        Action act = () => _validator.ValidateAndClone(document.RootElement);
        act.Should().Throw<ArgumentException>().WithMessage("*语义*");
    }
}
