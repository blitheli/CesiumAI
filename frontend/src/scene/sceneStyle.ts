import type { CzmlPacket } from "../contracts/chat";
import {
  MAX_SEMANTIC_JSON_BYTES,
  measureSemanticJsonSize,
} from "./semanticJsonSize";

const MAX_DEPTH = 12;
const MAX_ARRAY_LENGTH = 4096;

const ALLOWED_TOP_LEVEL_KEYS = new Set([
  "point",
  "path",
  "label",
  "billboard",
  "model",
  "polyline",
  "polygon",
  "ellipse",
]);

const NON_NEGATIVE_NUMERIC_KEYS = new Set([
  "width",
  "outlineWidth",
  "pixelSize",
  "scale",
]);

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function assertFiniteNumber(value: unknown): asserts value is number {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new Error("样式 patch 数值必须为有限数。");
  }
}

function validateRgba(rgba: unknown): void {
  if (!Array.isArray(rgba) || rgba.length !== 4) {
    throw new Error("rgba 必须是长度为 4 的数组。");
  }

  for (const component of rgba) {
    if (
      typeof component !== "number" ||
      !Number.isInteger(component) ||
      component < 0 ||
      component > 255
    ) {
      throw new Error("rgba 分量必须是 0..255 的整数。");
    }
  }
}

function validateNonNegativeNumber(propertyName: string, value: unknown): void {
  if (
    typeof value !== "number" ||
    !Number.isFinite(value) ||
    value < 0
  ) {
    throw new Error(`'${propertyName}' 必须是有限且非负的数值。`);
  }
}

function validateNode(
  node: unknown,
  depth: number,
  isTopLevel: boolean,
): void {
  if (depth > MAX_DEPTH) {
    throw new Error(`样式 patch 嵌套深度不能超过 ${MAX_DEPTH}。`);
  }

  if (isPlainObject(node)) {
    for (const [key, value] of Object.entries(node)) {
      if (isTopLevel && !ALLOWED_TOP_LEVEL_KEYS.has(key)) {
        throw new Error(`不允许的样式顶层属性：'${key}'。`);
      }

      if (value === null) {
        // 允许用 null 删除已允许的视觉字段。
        continue;
      }

      if (key === "rgba") {
        validateRgba(value);
        continue;
      }

      if (NON_NEGATIVE_NUMERIC_KEYS.has(key)) {
        validateNonNegativeNumber(key, value);
        continue;
      }

      validateNode(value, depth + 1, false);
    }
    return;
  }

  if (Array.isArray(node)) {
    if (node.length > MAX_ARRAY_LENGTH) {
      throw new Error(`样式 patch 数组长度不能超过 ${MAX_ARRAY_LENGTH}。`);
    }
    for (const item of node) {
      validateNode(item, depth + 1, false);
    }
    return;
  }

  if (typeof node === "number") {
    assertFiniteNumber(node);
    return;
  }

  if (
    typeof node === "string" ||
    typeof node === "boolean" ||
    node === null
  ) {
    return;
  }

  throw new Error(`不支持的 JSON 值类型：${typeof node}。`);
}

function validateStylePatch(patch: Record<string, unknown>): void {
  if (!isPlainObject(patch)) {
    throw new Error("样式 patch 必须是 JSON 对象。");
  }

  // 与后端 SemanticJsonSize 一致：按语义预算计量，避免 number 规范化膨胀导致前后端不一致。
  // 后端另有 raw UTF-8 传输加固；前端镜像语义预算，确保后端接受的 patch 前端也会接受（同拒同收）。
  const semanticBytes = measureSemanticJsonSize(patch);
  if (semanticBytes > MAX_SEMANTIC_JSON_BYTES) {
    throw new Error(
      `样式 patch 语义大小不能超过 ${MAX_SEMANTIC_JSON_BYTES} 字节。`,
    );
  }

  validateNode(patch, 1, true);
}

function deepMergeVisual(
  current: unknown,
  patch: unknown,
): unknown {
  if (patch === null) {
    return undefined;
  }

  if (Array.isArray(patch)) {
    return structuredClone(patch);
  }

  if (!isPlainObject(patch)) {
    return structuredClone(patch);
  }

  const base = isPlainObject(current) ? { ...current } : {};
  for (const [key, value] of Object.entries(patch)) {
    if (value === null) {
      delete base[key];
      continue;
    }

    if (Array.isArray(value)) {
      base[key] = structuredClone(value);
      continue;
    }

    if (isPlainObject(value)) {
      base[key] = deepMergeVisual(base[key], value);
      continue;
    }

    base[key] = structuredClone(value);
  }

  return base;
}

/**
 * 校验样式 patch（镜像后端白名单与限制），并不可变深合并到完整 packet。
 * 数组整体替换；对象递归合并；null 删除对应允许视觉字段；保留非视觉字段。
 */
export function applyStylePatch(
  packet: CzmlPacket,
  patch: Record<string, unknown>,
): CzmlPacket {
  validateStylePatch(patch);

  const next: CzmlPacket = structuredClone(packet);
  for (const [key, value] of Object.entries(patch)) {
    if (value === null) {
      delete next[key];
      continue;
    }

    if (Array.isArray(value)) {
      next[key] = structuredClone(value);
      continue;
    }

    if (isPlainObject(value)) {
      next[key] = deepMergeVisual(next[key], value);
      continue;
    }

    next[key] = structuredClone(value);
  }

  return next;
}
