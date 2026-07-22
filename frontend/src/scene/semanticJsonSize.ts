/** 前后端一致的语义 JSON 大小预算上限（32KiB）。（例如 style patch），防止超大补丁。 */
export const MAX_SEMANTIC_JSON_BYTES = 32 * 1024;

/** 任意有限 double 最坏十进制/科学计数表示的固定预算。 */
export const NUMBER_BUDGET_BYTES = 24;

const utf8Encoder = new TextEncoder();

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * 计量与 JSON 文本表示无关的语义大小,用来估算一段 JSON 值「有多胖」,避免超大补丁（与后端 SemanticJsonSize 对齐）：
 * - 对象：braces + commas + JSON-escaped UTF-8 key + colon + value
 * - 数组：brackets + commas + value
 * - 字符串/键：浏览器 JSON.stringify 后的 UTF-8（含 U+2028/U+2029 字面量）
 * - number：固定 24 bytes
 * - true/false/null：字面量长度
 */
export function measureSemanticJsonSize(value: unknown): number {
  if (value === null) {
    return 4;
  }

  if (typeof value === "boolean") {
    return value ? 4 : 5;
  }

  if (typeof value === "number") {
    return NUMBER_BUDGET_BYTES;
  }

  if (typeof value === "string") {
    return utf8Encoder.encode(JSON.stringify(value)).length;
  }

  if (Array.isArray(value)) {
    let size = 2; // []
    for (let index = 0; index < value.length; index++) {
      if (index > 0) {
        size += 1; // comma
      }
      size += measureSemanticJsonSize(value[index]);
    }
    return size;
  }

  if (isPlainObject(value)) {
    let size = 2; // {}
    let propertyCount = 0;
    for (const [key, child] of Object.entries(value)) {
      if (propertyCount > 0) {
        size += 1; // comma
      }
      size += utf8Encoder.encode(JSON.stringify(key)).length;
      size += 1; // colon
      size += measureSemanticJsonSize(child);
      propertyCount++;
    }
    return size;
  }

  throw new Error(`不支持的 JSON 值类型：${typeof value}。`);
}
