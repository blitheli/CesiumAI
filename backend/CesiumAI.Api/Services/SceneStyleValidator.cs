using System.Text;
using System.Text.Json;

namespace CesiumAI.Api.Services;

/// <summary>
/// 校验并克隆实体样式 patch，仅允许白名单视觉属性。
/// </summary>
public interface ISceneStyleValidator
{
    JsonElement ValidateAndClone(JsonElement patch);
}

public sealed class SceneStyleValidator : ISceneStyleValidator
{
    private const int MaxUtf8Bytes = 32 * 1024;
    private const int MaxDepth = 12;
    private const int MaxArrayLength = 4096;

    private static readonly HashSet<string> AllowedTopLevelKeys = new(StringComparer.Ordinal)
    {
        "point",
        "path",
        "label",
        "billboard",
        "model",
        "polyline",
        "polygon",
        "ellipse"
    };

    private static readonly HashSet<string> NonNegativeNumericKeys = new(StringComparer.Ordinal)
    {
        "width",
        "outlineWidth",
        "pixelSize",
        "scale"
    };

    public JsonElement ValidateAndClone(JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("样式 patch 必须是 JSON 对象。", nameof(patch));
        }

        byte[] utf8 = Encoding.UTF8.GetBytes(patch.GetRawText());
        if (utf8.Length > MaxUtf8Bytes)
        {
            throw new ArgumentException($"样式 patch 不能超过 {MaxUtf8Bytes} 字节。", nameof(patch));
        }

        ValidateNode(patch, depth: 1, isTopLevel: true);

        using JsonDocument cloneDocument = JsonDocument.Parse(utf8);
        return cloneDocument.RootElement.Clone();
    }

    private static void ValidateNode(JsonElement node, int depth, bool isTopLevel)
    {
        if (depth > MaxDepth)
        {
            throw new ArgumentException($"样式 patch 嵌套深度不能超过 {MaxDepth}。");
        }

        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in node.EnumerateObject())
                {
                    if (isTopLevel && !AllowedTopLevelKeys.Contains(property.Name))
                    {
                        throw new ArgumentException($"不允许的样式顶层属性：'{property.Name}'。");
                    }

                    if (property.Value.ValueKind == JsonValueKind.Null)
                    {
                        // 允许用 null 删除已允许的视觉字段。
                        continue;
                    }

                    if (string.Equals(property.Name, "rgba", StringComparison.Ordinal))
                    {
                        ValidateRgba(property.Value);
                        continue;
                    }

                    if (NonNegativeNumericKeys.Contains(property.Name))
                    {
                        ValidateNonNegativeNumber(property.Name, property.Value);
                        continue;
                    }

                    ValidateNode(property.Value, depth + 1, isTopLevel: false);
                }

                break;

            case JsonValueKind.Array:
                int length = 0;
                foreach (JsonElement item in node.EnumerateArray())
                {
                    length++;
                    if (length > MaxArrayLength)
                    {
                        throw new ArgumentException($"样式 patch 数组长度不能超过 {MaxArrayLength}。");
                    }

                    ValidateNode(item, depth + 1, isTopLevel: false);
                }

                break;

            case JsonValueKind.Number:
                if (!node.TryGetDouble(out double number) || !double.IsFinite(number))
                {
                    throw new ArgumentException("样式 patch 数值必须为有限数。");
                }

                break;

            case JsonValueKind.String:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                break;

            default:
                throw new ArgumentException($"不支持的 JSON 值类型：{node.ValueKind}。");
        }
    }

    private static void ValidateRgba(JsonElement rgba)
    {
        if (rgba.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("rgba 必须是长度为 4 的数组。");
        }

        int index = 0;
        foreach (JsonElement component in rgba.EnumerateArray())
        {
            index++;
            if (index > 4)
            {
                throw new ArgumentException("rgba 必须是长度为 4 的数组。");
            }

            // 要求 JSON 精确整数；拒绝任何小数（含极小正数）。
            if (component.ValueKind != JsonValueKind.Number
                || !component.TryGetInt32(out int value)
                || value < 0
                || value > 255)
            {
                throw new ArgumentException("rgba 分量必须是 0..255 的整数。");
            }
        }

        if (index != 4)
        {
            throw new ArgumentException("rgba 必须是长度为 4 的数组。");
        }
    }

    private static void ValidateNonNegativeNumber(string propertyName, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out double number)
            || !double.IsFinite(number)
            || number < 0)
        {
            throw new ArgumentException($"'{propertyName}' 必须是有限且非负的数值。");
        }
    }
}
