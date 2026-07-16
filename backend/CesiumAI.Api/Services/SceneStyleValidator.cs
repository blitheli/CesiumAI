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
    /// <summary>
    /// 额外的传输加固：拒绝超大 raw UTF-8 请求体。
    /// 这不是前后端共享的语义预算；所有被接受的 patch 仍必须通过
    /// <see cref="SemanticJsonSize.MaxSemanticBytes"/> 语义大小校验。
    /// </summary>
    private const int MaxRawUtf8Bytes = 32 * 1024;

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

    private static readonly HashSet<string> ExternalResourceContainers = new(StringComparer.Ordinal)
    {
        "billboard",
        "model"
    };

    private static readonly HashSet<string> ForbiddenExternalResourceKeys = new(StringComparer.Ordinal)
    {
        "image",
        "gltf",
        "uri",
        "url"
    };

    public JsonElement ValidateAndClone(JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("样式 patch 必须是 JSON 对象。", nameof(patch));
        }

        byte[] utf8 = Encoding.UTF8.GetBytes(patch.GetRawText());

        // 共享语义预算优先；与前端 measureSemanticJsonSize 对齐。
        int semanticBytes = SemanticJsonSize.Measure(patch);
        if (semanticBytes > SemanticJsonSize.MaxSemanticBytes)
        {
            throw new ArgumentException(
                $"样式 patch 语义大小不能超过 {SemanticJsonSize.MaxSemanticBytes} 字节。",
                nameof(patch));
        }

        // 额外传输加固（非语义预算）：限制原始 JSON 文本大小（如含大量空白的请求）。
        if (utf8.Length > MaxRawUtf8Bytes)
        {
            throw new ArgumentException(
                $"样式 patch 原始 UTF-8 不能超过 {MaxRawUtf8Bytes} 字节（传输加固）。",
                nameof(patch));
        }

        ValidateNode(patch, depth: 1, isTopLevel: true, insideBillboardOrModel: false);

        using JsonDocument cloneDocument = JsonDocument.Parse(utf8);
        return cloneDocument.RootElement.Clone();
    }

    private static void ValidateNode(
        JsonElement node,
        int depth,
        bool isTopLevel,
        bool insideBillboardOrModel)
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
                        // 允许用 null 删除已允许的视觉字段（含外部资源键）。
                        continue;
                    }

                    if (insideBillboardOrModel && ForbiddenExternalResourceKeys.Contains(property.Name))
                    {
                        throw new ArgumentException(
                            $"样式 patch 禁止在 billboard/model 内设置非 null 的外部资源字段：'{property.Name}'。");
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

                    bool nextInside = insideBillboardOrModel
                        || (isTopLevel && ExternalResourceContainers.Contains(property.Name));
                    ValidateNode(property.Value, depth + 1, isTopLevel: false, nextInside);
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

                    ValidateNode(item, depth + 1, isTopLevel: false, insideBillboardOrModel);
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

            // 与前端 Number.isInteger 对齐：接受 JSON `1.0` 这类精确整数写法，
            // 拒绝 1.5 / 非有限数（含 1e400→∞）。不用 TryGetInt32（其对 1.0 返回 false）。
            if (component.ValueKind != JsonValueKind.Number
                || !component.TryGetDouble(out double value)
                || !double.IsFinite(value)
                || !double.IsInteger(value)
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
