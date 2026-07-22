/**
 * CZML 实体视觉样式补丁：校验 + 不可变深合并。
 *
 * 由 `sceneDocument.reduceSceneDocument` 在处理 `op: "style"` 时调用
 * （SceneManager 再把归约后的完整 packet 同步到球上）。
 *
 * 只改允许的视觉字段（point / path / label / billboard / model / polyline / polygon / ellipse），
 * 不动 position、availability、properties、id 等轨道/业务数据。
 *
 * 合并语义：
 * - 对象：递归深合并（未出现在 patch 里的兄弟键保留）
 * - 数组（如 rgba）：整体替换，不按元素合并
 * - `null`：删除对应允许的视觉字段（顶层或嵌套均可）
 *
 * 校验与后端 SceneStyle 白名单镜像：顶层键、rgba、非负数值、嵌套深度、
 * 语义 JSON 体积，以及 billboard/model 内禁止设置非 null 外部资源 URI。
 * 详见 Docs/前端说明.md 中 style SceneOp 与 sceneStyle 相关说明。
 * 
 * 在 JavaScript 中，new Set 是用来创建一个唯一值集合（Set 对象）的语法，它最大的特点是，Set 对象会自动去重。
 */
import type { CzmlPacket } from "../contracts/chat";
import {
  MAX_SEMANTIC_JSON_BYTES,
  measureSemanticJsonSize,
} from "./semanticJsonSize";

/** patch 对象树允许的最大嵌套深度（含顶层；超过则拒绝）。 */
const MAX_DEPTH = 12;
/** patch 中任意数组的最大长度（防止超大 payload）。 */
const MAX_ARRAY_LENGTH = 4096;

/**
 * style patch 仅允许改这些顶层视觉容器。
 * 出现 `position` / `id` / `availability` / 未知键等会在校验阶段抛错。
 */
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

/**
 * 这些属性名出现时，值必须是有限且非负的 number
 * （如 path.width、point.pixelSize、model.scale）。
 */
const NON_NEGATIVE_NUMERIC_KEYS = new Set([
  "width",
  "outlineWidth",
  "pixelSize",
  "scale",
]);

/** 可能携带外部资源引用的视觉容器（进入后启用 URI 禁令）。
 * 
 * 主要是安全与可控：防止 Agent 通过 style 往场景里塞任意外部资源地址。所以前后端约定：style 只能改外观数值/颜色等，不能改资源指向。
 */
const EXTERNAL_RESOURCE_CONTAINERS = new Set(["billboard", "model"]);

/**
 * 在 billboard/model 内禁止用非 null 值设置这些键，
 * 避免 Agent 通过 style 注入任意 image/gltf URI。
 * 允许 `null`：用于删除实体上已有的外部资源字段。
 */
const FORBIDDEN_EXTERNAL_RESOURCE_KEYS = new Set([
  "image",
  "gltf",
  "uri",
  "url",
]);

/** 普通 JSON 对象（排除 null 与数组）。 */
function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/** 数值必须为有限 number（拒绝 NaN / Infinity）。 */
function assertFiniteNumber(value: unknown): asserts value is number {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new Error("样式 patch 数值必须为有限数。");
  }
}

/**
 * 校验 CZML 颜色数组：长度必须为 4，分量均为 0..255 的整数。
 * 例如 `[255, 0, 0, 255]`。
 */
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

/** 校验 width / pixelSize 等非负有限数值属性。 */
function validateNonNegativeNumber(propertyName: string, value: unknown): void {
  if (
    typeof value !== "number" ||
    !Number.isFinite(value) ||
    value < 0
  ) {
    throw new Error(`'${propertyName}' 必须是有限且非负的数值。`);
  }
}

/**
 * 递归校验 patch 子树的结构与取值约束。
 *
 * @param depth 当前深度（顶层为 1，不得超过 MAX_DEPTH）
 * @param isTopLevel 是否仍在 patch 根对象（决定是否检查 ALLOWED_TOP_LEVEL_KEYS）
 * @param insideBillboardOrModel 是否已进入 billboard/model，用于拦截外部资源键
 */
function validateNode(
  node: unknown,
  depth: number,
  isTopLevel: boolean,
  insideBillboardOrModel: boolean,
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
        // null 表示删除：允许出现在允许的视觉字段上（含外部资源键）。
        continue;
      }

      if (insideBillboardOrModel && FORBIDDEN_EXTERNAL_RESOURCE_KEYS.has(key)) {
        throw new Error(
          `样式 patch 禁止在 billboard/model 内设置非 null 的外部资源字段：'${key}'。`,
        );
      }

      if (key === "rgba") {
        validateRgba(value);
        continue;
      }

      if (NON_NEGATIVE_NUMERIC_KEYS.has(key)) {
        validateNonNegativeNumber(key, value);
        continue;
      }

      // 进入 billboard/model 后，后续嵌套一律视为「在容器内」。
      const nextInside =
        insideBillboardOrModel ||
        (isTopLevel && EXTERNAL_RESOURCE_CONTAINERS.has(key));
      validateNode(value, depth + 1, false, nextInside);
    }
    return;
  }

  if (Array.isArray(node)) {
    if (node.length > MAX_ARRAY_LENGTH) {
      throw new Error(`样式 patch 数组长度不能超过 ${MAX_ARRAY_LENGTH}。`);
    }
    for (const item of node) {
      validateNode(item, depth + 1, false, insideBillboardOrModel);
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

  // 拒绝函数、undefined、Symbol 等非 JSON 类型。
  throw new Error(`不支持的 JSON 值类型：${typeof node}。`);
}

/**
 * 入口校验：必须是普通对象，语义体积不超预算，再递归校验结构。
 *
 * 语义计量与后端 SemanticJsonSize 一致，保证「后端能收的前端也能收」。
 * 后端另有 raw UTF-8 传输加固；前端镜像语义预算即可。
 */
function validateStylePatch(patch: Record<string, unknown>): void {
  if (!isPlainObject(patch)) {
    throw new Error("样式 patch 必须是 JSON 对象。");
  }

  const semanticBytes = measureSemanticJsonSize(patch);
  if (semanticBytes > MAX_SEMANTIC_JSON_BYTES) {
    throw new Error(
      `样式 patch 语义大小不能超过 ${MAX_SEMANTIC_JSON_BYTES} 字节。`,
    );
  }

  validateNode(patch, 1, true, false);
}

/**
 * 将 patch 合并进某个视觉子树（如 path / point）。
 *
 * - `patch === null` → 返回 `undefined`（由调用方删除该键）
 * - 数组 → 整段深拷贝替换
 * - 对象 → 浅拷贝 base 后递归合并；子键为 `null` 则从 base 删除
 * - 标量 → 深拷贝替换
 *
 * 例：`{ width: 2, show: true }` + `{ width: 5 }` → `{ width: 5, show: true }`
 */
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
 * 对单个 CZML 实体应用视觉样式补丁：先校验，再不可变地合并进完整 packet。
 *
 * 调用方通常是 `reduceSceneDocument` 处理 `op: "style"` 时。本函数只负责「改内存 packet」，
 * 不涉及 Cesium DataSource；球上同步由 SceneManager 完成。
 *
 * 处理流程：
 * 1. `validateStylePatch`：顶层键白名单、rgba / 非负数、嵌套深度、语义体积、
 *    billboard/model 外部资源禁令等（与后端 SceneStyle 镜像，非法则抛错）
 * 2. 深拷贝 `packet`，再按 patch 各顶层键合并到副本上
 *
 * 合并规则（与 `deepMergeVisual` 一致）：
 * - 对象：递归深合并，未出现在 patch 中的兄弟键保留（如改 path.width 不丢 show）
 * - 数组：整段替换（如 rgba）
 * - `null`：删除该视觉字段（顶层删整个容器，嵌套删子键）
 * - `position` / `availability` / `id` 等非视觉字段不在白名单内，校验阶段即拒绝
 *
 * @param packet 目标实体的完整 CZML packet（含 position 等非视觉字段）
 * @param patch 仅含允许视觉键的补丁对象
 * @returns 新 packet；入参 `packet` 不被修改。非视觉字段原样保留。
 *
 * @example
 * applyStylePatch(
 *   { id: "iss", position: { cartesian: [1, 2, 3] }, path: { width: 2, show: true } },
 *   { path: { width: 5 }, point: { pixelSize: 12 }, label: null },
 * );
 * // → path.width=5 且保留 show；新增/合并 point；删除 label；position 不变
 */
export function applyStylePatch(
  packet: CzmlPacket,
  patch: Record<string, unknown>,
): CzmlPacket {
  validateStylePatch(patch);

  // 先深拷贝整包，保证调用方持有的 packet 不被原地修改。
  const next: CzmlPacket = structuredClone(packet);
  for (const [key, value] of Object.entries(patch)) {
    if (value === null) {
      // 顶层 null：删除整个视觉容器，例如 { label: null }。
      delete next[key];
      continue;
    }

    if (Array.isArray(value)) {
      next[key] = structuredClone(value);
      continue;
    }

    if (isPlainObject(value)) {
      // 对象：与现有同名容器深合并（无则相当于新建）。
      next[key] = deepMergeVisual(next[key], value);
      continue;
    }

    next[key] = structuredClone(value);
  }

  return next;
}
