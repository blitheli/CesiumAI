using System.Globalization;
using System.Text.Json;

namespace CesiumAI.Api.Astrox;

/// <summary>
/// 在发往 Astrox 前校验通用传播请求根对象的 Start/Stop/Step。
/// </summary>
public static class PropagationRequestValidator
{
    private static readonly string[] RequiredKeys = ["Start", "Stop", "Step"];

    /// <summary>
    /// 校验请求根对象：精确键、无语义重复、UTC、&lt;=24h、Step 1..3600，并与 Tool 声明窗口一致。
    /// </summary>
    public static void Validate(
        JsonElement request,
        DateTimeOffset startUtc,
        DateTimeOffset stopUtc)
    {
        if (request.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("传播请求必须是 JSON 对象。", nameof(request));
        }

        EnsureNoSemanticDuplicates(request);

        if (!TryGetExact(request, "Start", out JsonElement startElement)
            || !TryGetExact(request, "Stop", out JsonElement stopElement)
            || !TryGetExact(request, "Step", out JsonElement stepElement))
        {
            throw new ArgumentException(
                "传播请求根对象必须包含精确键 Start、Stop、Step；缺少任一字段时为安全起见拒绝。",
                nameof(request));
        }

        DateTimeOffset requestStart = ParseRequiredUtc(startElement, "Start");
        DateTimeOffset requestStop = ParseRequiredUtc(stopElement, "Stop");
        int step = ParseRequiredStep(stepElement);

        DateTimeOffset expectedStart = startUtc.ToUniversalTime();
        DateTimeOffset expectedStop = stopUtc.ToUniversalTime();

        if (requestStart != expectedStart || requestStop != expectedStop)
        {
            throw new ArgumentException(
                "传播请求根对象的 Start/Stop 必须与 Tool 声明的 startUtc/stopUtc 一致。",
                nameof(request));
        }

        if (requestStop <= requestStart)
        {
            throw new ArgumentException("传播请求 Stop 必须晚于 Start。", nameof(request));
        }

        if (requestStop - requestStart > TimeSpan.FromHours(24))
        {
            throw new ArgumentException("传播请求窗口不能超过 24 小时。", nameof(request));
        }

        if (step is < 1 or > 3600)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                step,
                "传播请求 Step 必须是 1..3600 的整数秒。");
        }
    }

    private static void EnsureNoSemanticDuplicates(JsonElement request)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in request.EnumerateObject())
        {
            bool isRequiredFamily = RequiredKeys.Any(key =>
                string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase));
            if (!isRequiredFamily)
            {
                continue;
            }

            if (!seen.Add(property.Name))
            {
                throw new ArgumentException(
                    $"传播请求根对象不能包含语义重复键（含大小写变体）：'{property.Name}'。",
                    nameof(request));
            }

            // 大小写变体（如 start / START）视为语义重复，必须使用精确 Start/Stop/Step。
            string? canonical = RequiredKeys.FirstOrDefault(key =>
                string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase));
            if (canonical is not null
                && !string.Equals(property.Name, canonical, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"传播请求根键 '{property.Name}' 必须使用精确大小写 '{canonical}'。",
                    nameof(request));
            }
        }
    }

    private static DateTimeOffset ParseRequiredUtc(JsonElement element, string key)
    {
        if (element.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(element.GetString())
            || !DateTimeOffset.TryParse(
                element.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed))
        {
            throw new ArgumentException($"传播请求 {key} 必须是有效 UTC 时间戳。", nameof(element));
        }

        return parsed.ToUniversalTime();
    }

    private static int ParseRequiredStep(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int step))
        {
            throw new ArgumentException("传播请求 Step 必须是整数秒。", nameof(element));
        }

        return step;
    }

    private static bool TryGetExact(JsonElement element, string propertyName, out JsonElement value)
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
