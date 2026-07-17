using System.Text.Json;
using CesiumAI.Api.Astrox;
using FluentAssertions;

namespace CesiumAI.Api.Tests.Astrox;

public class PropagationRequestValidatorTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-07-16T00:00:00Z");
    private static readonly DateTimeOffset Stop = DateTimeOffset.Parse("2026-07-16T01:00:00Z");

    [Fact]
    public void Validate_AcceptsExactStartStopStepMatchingToolWindow()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "Start": "2026-07-16T00:00:00.000Z",
              "Stop": "2026-07-16T01:00:00.000Z",
              "Step": 60,
              "CentralBody": "Earth"
            }
            """);

        Action act = () => PropagationRequestValidator.Validate(document.RootElement, Start, Stop);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("""{"Stop":"2026-07-16T01:00:00Z","Step":60}""")]
    [InlineData("""{"Start":"2026-07-16T00:00:00Z","Step":60}""")]
    [InlineData("""{"Start":"2026-07-16T00:00:00Z","Stop":"2026-07-16T01:00:00Z"}""")]
    [InlineData("""{"CentralBody":"Earth"}""")]
    public void Validate_RejectsMissingRequiredKeys_ForSafety(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        Action act = () => PropagationRequestValidator.Validate(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("""{"start":"2026-07-16T00:00:00Z","Stop":"2026-07-16T01:00:00Z","Step":60}""")]
    [InlineData("""{"Start":"2026-07-16T00:00:00Z","stop":"2026-07-16T01:00:00Z","Step":60}""")]
    [InlineData("""{"Start":"2026-07-16T00:00:00Z","Stop":"2026-07-16T01:00:00Z","STEP":60}""")]
    [InlineData("""{"Start":"2026-07-16T00:00:00Z","Stop":"2026-07-16T01:00:00Z","Step":60,"start":"2026-07-16T00:00:00Z"}""")]
    public void Validate_RejectsWrongCasingOrSemanticDuplicates(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        Action act = () => PropagationRequestValidator.Validate(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_RejectsMultiYearRequestWindow_WithoutMatchingToolParams()
    {
        // Tool 声明 1 小时，请求根对象却跨多年——伪装/不一致必须拒绝。
        using JsonDocument document = JsonDocument.Parse("""
            {
              "Start": "2020-01-01T00:00:00Z",
              "Stop": "2026-07-16T00:00:00Z",
              "Step": 60
            }
            """);

        Action act = () => PropagationRequestValidator.Validate(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_RejectsOneSecondRequestDisguisedAsOneHourToolWindow()
    {
        // Tool 声明 1 小时，请求 Start/Stop 仅相隔 1 秒——不一致必须拒绝。
        using JsonDocument document = JsonDocument.Parse("""
            {
              "Start": "2026-07-16T00:00:00Z",
              "Stop": "2026-07-16T00:00:01Z",
              "Step": 1
            }
            """);

        Action act = () => PropagationRequestValidator.Validate(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public void Validate_RejectsStepOutsideOneTo3600(int step)
    {
        using JsonDocument document = JsonDocument.Parse($$"""
            {
              "Start": "2026-07-16T00:00:00Z",
              "Stop": "2026-07-16T01:00:00Z",
              "Step": {{step}}
            }
            """);

        Action act = () => PropagationRequestValidator.Validate(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_RejectsNonIntegerStep()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "Start": "2026-07-16T00:00:00Z",
              "Stop": "2026-07-16T01:00:00Z",
              "Step": 60.5
            }
            """);

        Action act = () => PropagationRequestValidator.Validate(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_RejectsNonObjectRequest()
    {
        using JsonDocument document = JsonDocument.Parse("""["Start"]""");

        Action act = () => PropagationRequestValidator.Validate(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }
}
