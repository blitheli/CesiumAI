using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CesiumAI.Api.Services;

/// <summary>
/// 前后端一致的语义 JSON 大小预算（与表示法无关）。
/// 字符串/属性名按浏览器 <c>JSON.stringify</c> 规则计量。
/// </summary>
public static class SemanticJsonSize
{
    public const int MaxSemanticBytes = 32 * 1024;

    /// <summary>
    /// 任意有限 double 最坏十进制/科学计数表示的固定预算。
    /// </summary>
    public const int NumberBudgetBytes = 24;

    /// <summary>
    /// 与浏览器 JSON.stringify 对齐：UnsafeRelaxed 不转义 &lt;/非 ASCII，
    /// 再将 U+2028/U+2029 的 <c>\u2028</c>/<c>\u2029</c> 还原为字面 UTF-8（Relaxed 默认仍会转义它们）。
    /// </summary>
    private static readonly JsonSerializerOptions BrowserAlignedStringOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static int Measure(JsonElement node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
            {
                int size = 2; // {}
                int propertyCount = 0;
                foreach (JsonProperty property in node.EnumerateObject())
                {
                    if (propertyCount > 0)
                    {
                        size += 1; // comma
                    }

                    size += MeasureBrowserJsonString(property.Name);
                    size += 1; // colon
                    size += Measure(property.Value);
                    propertyCount++;
                }

                return size;
            }

            case JsonValueKind.Array:
            {
                int size = 2; // []
                int index = 0;
                foreach (JsonElement item in node.EnumerateArray())
                {
                    if (index > 0)
                    {
                        size += 1; // comma
                    }

                    size += Measure(item);
                    index++;
                }

                return size;
            }

            case JsonValueKind.String:
            {
                // 取语义字符串，禁止用 GetRawText（原始 \uXXXX / 转义会影响字节数）。
                string? text = node.GetString();
                ArgumentNullException.ThrowIfNull(text);
                return MeasureBrowserJsonString(text);
            }

            case JsonValueKind.Number:
                return NumberBudgetBytes;

            case JsonValueKind.True:
                return 4;

            case JsonValueKind.False:
                return 5;

            case JsonValueKind.Null:
                return 4;

            default:
                throw new ArgumentException($"不支持的 JSON 值类型：{node.ValueKind}。");
        }
    }

    /// <summary>
    /// 计量 JSON 字符串字面量的 UTF-8 字节数（含引号），对齐 <c>JSON.stringify</c>。
    /// </summary>
    public static int MeasureBrowserJsonString(string value)
    {
        string encoded = JsonSerializer.Serialize(value, BrowserAlignedStringOptions);
        encoded = AlignLineSeparatorEscapesWithBrowser(encoded);
        return Encoding.UTF8.GetByteCount(encoded);
    }

    /// <summary>
    /// 将 UnsafeRelaxed 产出的 <c>\u2028</c>/<c>\u2029</c> 还原为字面字符；
    /// 不误伤语义上的 <c>\\u2028</c>（字面反斜杠序列）。
    /// </summary>
    private static string AlignLineSeparatorEscapesWithBrowser(string jsonEncodedString)
    {
        if (jsonEncodedString.Length < 2
            || jsonEncodedString[0] != '"'
            || jsonEncodedString[^1] != '"')
        {
            throw new ArgumentException("期望 JsonSerializer 产出的 JSON 字符串字面量。");
        }

        if (!jsonEncodedString.Contains("\\u2028", StringComparison.Ordinal)
            && !jsonEncodedString.Contains("\\u2029", StringComparison.Ordinal))
        {
            return jsonEncodedString;
        }

        var builder = new StringBuilder(jsonEncodedString.Length);
        builder.Append('"');

        for (int i = 1; i < jsonEncodedString.Length - 1;)
        {
            char current = jsonEncodedString[i];
            if (current == '\\' && i + 5 < jsonEncodedString.Length - 1
                && jsonEncodedString[i + 1] == 'u'
                && jsonEncodedString[i + 2] == '2'
                && jsonEncodedString[i + 3] == '0'
                && jsonEncodedString[i + 4] == '2'
                && (jsonEncodedString[i + 5] == '8' || jsonEncodedString[i + 5] == '9'))
            {
                builder.Append(jsonEncodedString[i + 5] == '8' ? '\u2028' : '\u2029');
                i += 6;
                continue;
            }

            if (current == '\\' && i + 1 < jsonEncodedString.Length - 1)
            {
                char escaped = jsonEncodedString[i + 1];
                builder.Append('\\');
                builder.Append(escaped);
                if (escaped == 'u' && i + 5 < jsonEncodedString.Length - 1)
                {
                    builder.Append(jsonEncodedString[i + 2]);
                    builder.Append(jsonEncodedString[i + 3]);
                    builder.Append(jsonEncodedString[i + 4]);
                    builder.Append(jsonEncodedString[i + 5]);
                    i += 6;
                }
                else
                {
                    i += 2;
                }

                continue;
            }

            builder.Append(current);
            i++;
        }

        builder.Append('"');
        return builder.ToString();
    }
}
