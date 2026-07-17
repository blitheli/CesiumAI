using System.Text;
using System.Text.Json;
using CesiumAI.Api.Astrox;
using FluentAssertions;

namespace CesiumAI.Api.Tests.Astrox;

public class CzmlPositionValidatorTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-07-16T00:00:00Z");
    private static readonly DateTimeOffset Stop = DateTimeOffset.Parse("2026-07-16T01:00:00Z");

    private readonly ICzmlPositionValidator _validator = new CzmlPositionValidator();

    [Fact]
    public void ValidateAndClone_AcceptsEpochPlusCartesianStride4()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "epoch": "2026-07-16T00:00:00.000Z",
              "cartesian": [0, 1, 2, 3, 60, 4, 5, 6]
            }
            """);

        JsonElement clone = _validator.ValidateAndClone(document.RootElement, Start, Stop);

        clone.GetProperty("epoch").GetString().Should().Be("2026-07-16T00:00:00.000Z");
        clone.GetProperty("cartesian")
            .EnumerateArray()
            .Select(value => value.GetDouble())
            .Should()
            .Equal([0, 1, 2, 3, 60, 4, 5, 6]);
    }

    [Fact]
    public void ValidateAndClone_AcceptsEpochPlusCartesianVelocityStride7()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "epoch": "2026-07-16T00:00:00.000Z",
              "cartesianVelocity": [0, 1, 2, 3, 4, 5, 6, 60, 7, 8, 9, 10, 11, 12]
            }
            """);

        JsonElement clone = _validator.ValidateAndClone(document.RootElement, Start, Stop);

        clone.GetProperty("cartesianVelocity").GetArrayLength().Should().Be(14);
    }

    [Fact]
    public void ValidateAndClone_PositionRemainsReadableAfterSourceDocumentDisposed()
    {
        JsonElement position;
        {
            using JsonDocument document = JsonDocument.Parse("""
                {
                  "epoch": "2026-07-16T00:00:00.000Z",
                  "cartesian": [0, 1, 2, 3]
                }
                """);
            position = _validator.ValidateAndClone(document.RootElement, Start, Stop);
        }

        // 源 JsonDocument 已释放后，返回的 Position 仍须可读。
        position.GetProperty("epoch").GetString().Should().Be("2026-07-16T00:00:00.000Z");
        position.GetProperty("cartesian")
            .EnumerateArray()
            .Select(value => value.GetDouble())
            .Should()
            .Equal([0, 1, 2, 3]);
    }

    [Theory]
    [InlineData("""{"epoch":"2026-07-16T00:00:00.000Z","Cartesian":[0,1,2,3]}""")]
    [InlineData("""{"EPOCH":"2026-07-16T00:00:00.000Z","cartesian":[0,1,2,3]}""")]
    [InlineData("""{"epoch":"2026-07-16T00:00:00.000Z","cartesianVelocity":[0,1,2,3,4,5,6],"CartesianVelocity":[0,1,2,3,4,5,6]}""")]
    [InlineData("""{"epoch":"2026-07-16T00:00:00.000Z","cartesian":[0,1,2,3],"Cartesian":[9,9,9,9]}""")]
    [InlineData("""{"epoch":"2026-07-16T00:00:00.000Z","Epoch":"2026-07-16T00:00:00.000Z","cartesian":[0,1,2,3]}""")]
    public void ValidateAndClone_RejectsWrongCasedOrDuplicateCaseVariantKeys(string positionJson)
    {
        using JsonDocument document = JsonDocument.Parse(positionJson);

        Action act = () => _validator.ValidateAndClone(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("""{"cartesian":[0,1,2,3]}""")]
    [InlineData("""{"epoch":"","cartesian":[0,1,2,3]}""")]
    [InlineData("""{"epoch":"not-a-date","cartesian":[0,1,2,3]}""")]
    [InlineData("""{"epoch":"2026-07-16T00:00:00.000Z"}""")]
    [InlineData("""{"epoch":"2026-07-16T00:00:00.000Z","cartesian":[]}""")]
    [InlineData("""{"epoch":"2026-07-16T00:00:00.000Z","cartesian":[0,1,2]}""")]
    [InlineData("""{"epoch":"2026-07-16T00:00:00.000Z","cartesianVelocity":[0,1,2,3,4,5]}""")]
    public void ValidateAndClone_RejectsMissingEpochEmptyOrWrongStride(string positionJson)
    {
        using JsonDocument document = JsonDocument.Parse(positionJson);

        Action act = () => _validator.ValidateAndClone(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateAndClone_RejectsBothSamplingFields()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "epoch": "2026-07-16T00:00:00.000Z",
              "cartesian": [0, 1, 2, 3],
              "cartesianVelocity": [0, 1, 2, 3, 4, 5, 6]
            }
            """);

        Action act = () => _validator.ValidateAndClone(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateAndClone_RejectsNonNumericSampleValues()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "epoch": "2026-07-16T00:00:00.000Z",
              "cartesian": [0, 1, "bad", 3]
            }
            """);

        Action act = () => _validator.ValidateAndClone(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("""{"epoch":"2026-07-16T00:00:00.000Z","cartesian":[0,NaN,2,3]}""")]
    [InlineData("""{"epoch":"2026-07-16T00:00:00.000Z","cartesian":[0,1,Infinity,3]}""")]
    [InlineData("""{"epoch":"2026-07-16T00:00:00.000Z","cartesian":[0,1,-Infinity,3]}""")]
    public void StandardJsonParser_RejectsNamedNonFiniteLiterals(string positionJson)
    {
        // 标准 JSON 不支持 NaN/Infinity 字面量；应在解析阶段失败，而不是进入 validator。
        Action act = () => JsonDocument.Parse(positionJson);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void ValidateAndClone_RejectsOverflowingJsonNumber_WhenTryGetDoubleFails()
    {
        // 标准 JSON Number 溢出（如 1e400）：ValueKind 仍为 Number，但 TryGetDouble 返回 false。
        using JsonDocument document = JsonDocument.Parse("""
            {
              "epoch": "2026-07-16T00:00:00.000Z",
              "cartesian": [0, 1e400, 2, 3]
            }
            """);

        Action act = () => _validator.ValidateAndClone(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateAndClone_RejectsOverflowingTimeOffsetJsonNumber()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "epoch": "2026-07-16T00:00:00.000Z",
              "cartesian": [1e400, 1, 2, 3]
            }
            """);

        Action act = () => _validator.ValidateAndClone(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void ValidateAndClone_RejectsNegativeOrDuplicateTimeOffsets(double secondOffset)
    {
        // secondOffset=0 用于与首样本 offset=0 重复。
        string positionJson = secondOffset < 0
            ? """{"epoch":"2026-07-16T00:00:00.000Z","cartesian":[-1,1,2,3]}"""
            : """{"epoch":"2026-07-16T00:00:00.000Z","cartesian":[0,1,2,3,0,4,5,6]}""";

        using JsonDocument document = JsonDocument.Parse(positionJson);

        Action act = () => _validator.ValidateAndClone(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateAndClone_RejectsNonStrictlyIncreasingTimeOffsets()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "epoch": "2026-07-16T00:00:00.000Z",
              "cartesian": [0, 1, 2, 3, 60, 4, 5, 6, 30, 7, 8, 9]
            }
            """);

        Action act = () => _validator.ValidateAndClone(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateAndClone_RejectsSamplesOutsideAvailabilityWindow()
    {
        // stop 为 01:00，偏移 3601 秒超出 availability。
        using JsonDocument document = JsonDocument.Parse("""
            {
              "epoch": "2026-07-16T00:00:00.000Z",
              "cartesian": [0, 1, 2, 3, 3601, 4, 5, 6]
            }
            """);

        Action act = () => _validator.ValidateAndClone(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateAndClone_RejectsAvailabilityLongerThan24Hours()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "epoch": "2026-07-16T00:00:00.000Z",
              "cartesian": [0, 1, 2, 3]
            }
            """);
        DateTimeOffset longStop = Start.AddHours(24).AddSeconds(1);

        Action act = () => _validator.ValidateAndClone(document.RootElement, Start, longStop);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateAndClone_RejectsMoreThan10000Samples()
    {
        var samples = new StringBuilder();
        for (int i = 0; i < 10001; i++)
        {
            if (i > 0)
            {
                samples.Append(',');
            }

            samples.Append(CultureInvariant($"{i},1,2,3"));
        }

        string positionJson = $$"""
            {
              "epoch": "2026-07-16T00:00:00.000Z",
              "cartesian": [{{samples}}]
            }
            """;
        using JsonDocument document = JsonDocument.Parse(positionJson);
        DateTimeOffset wideStop = Start.AddHours(24);

        Action act = () => _validator.ValidateAndClone(document.RootElement, Start, wideStop);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateAndClone_RejectsPositionJsonLargerThan2MiB()
    {
        // 构造超过 2 MiB 的合法 stride-4 数组文本。
        var samples = new StringBuilder(capacity: 2 * 1024 * 1024 + 1024);
        int sampleCount = 0;
        while (samples.Length < (2 * 1024 * 1024))
        {
            if (sampleCount > 0)
            {
                samples.Append(',');
            }

            samples.Append(CultureInvariant($"{sampleCount},1,2,3"));
            sampleCount++;
            if (sampleCount > 10000)
            {
                break;
            }
        }

        // 若样本上限先触发，改用超大填充字段确保命中 2 MiB 限制。
        string padding = new string('x', (2 * 1024 * 1024) + 8);
        string positionJson = $$"""
            {
              "epoch": "2026-07-16T00:00:00.000Z",
              "cartesian": [0, 1, 2, 3],
              "pad": "{{padding}}"
            }
            """;
        using JsonDocument document = JsonDocument.Parse(positionJson);

        Action act = () => _validator.ValidateAndClone(document.RootElement, Start, Stop);

        act.Should().Throw<ArgumentException>();
    }

    private static string CultureInvariant(FormattableString value)
        => FormattableString.Invariant(value);
}
