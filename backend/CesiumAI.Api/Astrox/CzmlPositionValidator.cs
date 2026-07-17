using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CesiumAI.Api.Astrox;

/// <summary>
/// 校验并克隆标准 CZML Position，限制样本数量、大小与 availability 窗口。
/// </summary>
public interface ICzmlPositionValidator
{
    JsonElement ValidateAndClone(JsonElement position, DateTimeOffset start, DateTimeOffset stop);
}

public sealed class CzmlPositionValidator : ICzmlPositionValidator
{
    private const int MaxUtf8Bytes = 2 * 1024 * 1024;
    private const int MaxSamples = 10_000;
    private static readonly TimeSpan MaxAvailability = TimeSpan.FromHours(24);

    public JsonElement ValidateAndClone(JsonElement position, DateTimeOffset start, DateTimeOffset stop)
    {
        DateTimeOffset startUtc = start.ToUniversalTime();
        DateTimeOffset stopUtc = stop.ToUniversalTime();

        if (stopUtc <= startUtc)
        {
            throw new ArgumentException("Availability stop 必须晚于 start。");
        }

        if (stopUtc - startUtc > MaxAvailability)
        {
            throw new ArgumentException("Availability 窗口不能超过 24 小时。");
        }

        if (position.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Position 必须是 JSON 对象。", nameof(position));
        }

        byte[] utf8 = Encoding.UTF8.GetBytes(position.GetRawText());
        if (utf8.Length > MaxUtf8Bytes)
        {
            throw new ArgumentException($"Position JSON 不能超过 {MaxUtf8Bytes} 字节。", nameof(position));
        }

        EnsureNoCaseInsensitiveDuplicateNames(position, "Position");

        if (!TryGetPropertyExact(position, "epoch", out JsonElement epochElement)
            || epochElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(epochElement.GetString())
            || !DateTimeOffset.TryParse(
                epochElement.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset epoch))
        {
            throw new ArgumentException("Position.epoch 必须是有效的 UTC 时间戳。", nameof(position));
        }

        bool hasCartesian = TryGetPropertyExact(position, "cartesian", out JsonElement cartesian);
        bool hasCartesianVelocity = TryGetPropertyExact(
            position,
            "cartesianVelocity",
            out JsonElement cartesianVelocity);

        if (hasCartesian == hasCartesianVelocity)
        {
            throw new ArgumentException(
                "Position 必须恰好包含 cartesian 或 cartesianVelocity 其中之一。",
                nameof(position));
        }

        if (hasCartesian)
        {
            ValidateSamples(cartesian, stride: 4, epoch, startUtc, stopUtc, nameof(position));
        }
        else
        {
            ValidateSamples(cartesianVelocity, stride: 7, epoch, startUtc, stopUtc, nameof(position));
        }

        using JsonDocument cloneDocument = JsonDocument.Parse(utf8);
        return cloneDocument.RootElement.Clone();
    }

    private static void ValidateSamples(
        JsonElement samples,
        int stride,
        DateTimeOffset epoch,
        DateTimeOffset startUtc,
        DateTimeOffset stopUtc,
        string parameterName)
    {
        if (samples.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("Position 采样字段必须是数组。", parameterName);
        }

        int length = samples.GetArrayLength();
        if (length == 0 || length % stride != 0)
        {
            throw new ArgumentException(
                $"Position 采样数组必须非空且长度为 {stride} 的倍数。",
                parameterName);
        }

        int sampleCount = length / stride;
        if (sampleCount > MaxSamples)
        {
            throw new ArgumentException($"Position 样本数不能超过 {MaxSamples}。", parameterName);
        }

        double? previousOffset = null;
        int index = 0;
        foreach (JsonElement value in samples.EnumerateArray())
        {
            if (!TryGetFiniteDouble(value, out double number))
            {
                throw new ArgumentException("Position 采样值必须是有限数值。", parameterName);
            }

            if (index % stride == 0)
            {
                double offsetSeconds = number;
                if (offsetSeconds < 0
                    || (previousOffset is not null && offsetSeconds <= previousOffset.Value))
                {
                    throw new ArgumentException(
                        "Position 时间偏移必须非负且严格递增。",
                        parameterName);
                }

                DateTimeOffset sampleTime;
                try
                {
                    sampleTime = epoch.AddSeconds(offsetSeconds);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    throw new ArgumentException("Position 时间偏移超出可表示范围。", parameterName, ex);
                }

                if (sampleTime < startUtc || sampleTime > stopUtc)
                {
                    throw new ArgumentException(
                        "Position 样本时间必须位于 availability 窗口内。",
                        parameterName);
                }

                previousOffset = offsetSeconds;
            }

            index++;
        }
    }

    private static bool TryGetFiniteDouble(JsonElement value, out double number)
    {
        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out number)
            && double.IsFinite(number))
        {
            return true;
        }

        number = default;
        return false;
    }

    private static void EnsureNoCaseInsensitiveDuplicateNames(JsonElement obj, string context)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in obj.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new ArgumentException(
                    $"{context} 不能包含重复键（含大小写变体）：'{property.Name}'。");
            }
        }
    }

    private static bool TryGetPropertyExact(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.Ordinal))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
