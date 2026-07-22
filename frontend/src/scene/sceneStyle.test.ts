import type { CzmlPacket } from "../contracts/chat";
import {
  MAX_SEMANTIC_JSON_BYTES,
  measureSemanticJsonSize,
} from "./semanticJsonSize";
import { applyStylePatch } from "./sceneStyle";

/** 含轨道数据与视觉字段的基准实体，用于验证合并时非视觉字段不被改动。 */
const basePacket: CzmlPacket = {
  id: "iss",
  name: "ISS",
  availability: "2026-07-16T00:00:00Z/2026-07-17T00:00:00Z",
  position: {
    epoch: "2026-07-16T00:00:00Z",
    cartesian: [1, 2, 3, 4, 5, 6, 7],
  },
  properties: { orbitHint: "sgp4" },
  path: { width: 2, show: true, material: { solidColor: { color: { rgba: [0, 255, 0, 255] } } } },
  point: { pixelSize: 8, color: { rgba: [255, 255, 255, 255] } },
  label: { text: "ISS" },
};

// 允许的视觉字段深合并；position / availability / id 等非视觉数据必须原样保留。
it("deep-merges allowed visual fields while preserving non-visual packet data", () => {
  const result = applyStylePatch(basePacket, {
    path: { width: 5, material: { solidColor: { color: { rgba: [255, 0, 0, 255] } } } },
    point: { pixelSize: 12 },
  });

  expect(result.position).toEqual(basePacket.position);
  expect(result.availability).toBe(basePacket.availability);
  expect(result.properties).toEqual(basePacket.properties);
  expect(result.id).toBe("iss");
  expect(result.name).toBe("ISS");
  expect(result.path).toEqual({
    width: 5,
    show: true,
    material: { solidColor: { color: { rgba: [255, 0, 0, 255] } } },
  });
  expect(result.point).toEqual({
    pixelSize: 12,
    color: { rgba: [255, 255, 255, 255] },
  });
  expect(result.label).toEqual({ text: "ISS" });
});

// 数组（如 rgba）整段替换，不按下标逐元素合并。
it("replaces arrays wholesale instead of merging by index", () => {
  const result = applyStylePatch(basePacket, {
    point: { color: { rgba: [1, 2, 3, 4] } },
  });

  expect(result.point).toEqual({
    pixelSize: 8,
    color: { rgba: [1, 2, 3, 4] },
  });
});

// patch 值为 null：顶层删除整个容器，嵌套删除子键；非视觉字段不受影响。
it("deletes allowed visual fields when patch values are null", () => {
  const result = applyStylePatch(basePacket, {
    label: null,
    path: { width: null },
  });

  expect(result.label).toBeUndefined();
  expect(result.path).toEqual({
    show: true,
    material: { solidColor: { color: { rgba: [0, 255, 0, 255] } } },
  });
  expect(result.position).toEqual(basePacket.position);
});

// 返回新 packet，不得原地修改入参。
it("does not mutate the original packet", () => {
  const original = structuredClone(basePacket);
  applyStylePatch(basePacket, { path: { width: 9 } });
  expect(basePacket).toEqual(original);
});

// 顶层禁止 id / position / availability / properties 及未知键。
it.each(["id", "position", "availability", "properties", "unknown"] as const)(
  "rejects forbidden or unknown top-level key %s",
  (key) => {
    expect(() => applyStylePatch(basePacket, { [key]: {} })).toThrow();
  },
);

// rgba 必须是长度 4、分量 0..255 的整数；长度/范围/小数均应拒绝。
it.each([
  "[255,0,0]",
  "[255,0,0,255,1]",
  "[256,0,0,255]",
  "[-1,0,0,255]",
  "[255.5,0,0,255]",
  "[0.0000001,0,0,255]",
  "[1.1,2,3,4]",
])("rejects invalid rgba arrays %s", (rgbaJson) => {
  const patch = JSON.parse(
    `{"point":{"color":{"rgba":${rgbaJson}}}}`,
  ) as Record<string, unknown>;
  expect(() => applyStylePatch(basePacket, patch)).toThrow();
});

// width / pixelSize / outlineWidth / scale 等不得为负数。
it.each([
  { path: { width: -1 } },
  { point: { pixelSize: -0.5 } },
  { point: { outlineWidth: -2 } },
  { billboard: { scale: -0.01 } },
])("rejects negative numeric visual values %j", (patch) => {
  expect(() => applyStylePatch(basePacket, patch)).toThrow();
});

// Infinity / NaN 不是有限数，必须拒绝。
it("rejects non-finite numbers", () => {
  expect(() =>
    applyStylePatch(basePacket, { path: { width: Number.POSITIVE_INFINITY } }),
  ).toThrow();
  expect(() =>
    applyStylePatch(basePacket, { path: { width: Number.NaN } }),
  ).toThrow();
});

// 超过 32 KiB 语义 JSON 预算的超大字符串应拒绝。
it("rejects payloads larger than the 32 KiB semantic JSON budget", () => {
  const largeText = "a".repeat(33 * 1024);
  expect(() =>
    applyStylePatch(basePacket, { label: { text: largeText } }),
  ).toThrow(/语义|32/);
});

// 大量科学计数数字：raw JSON 虽短，语义体积按 number 计费会超预算（与后端一致）。
it("rejects 4096 scientific numbers by semantic budget (same as backend)", () => {
  // raw `1e20` 很短，JSON.stringify 可能膨胀；语义预算对每个 number 固定计 24。
  const cartesian = Array.from({ length: 4096 }, () => 1e20);
  const patch = { polyline: { positions: { cartesian } } };
  expect(measureSemanticJsonSize(patch)).toBeGreaterThan(MAX_SEMANTIC_JSON_BYTES);
  expect(() => applyStylePatch(basePacket, patch)).toThrow(/语义/);
});

// 小体积合法 patch 应在预算内且顺利通过。
it("accepts a small boundary object under the semantic budget", () => {
  const patch = { path: { width: 5 }, point: { pixelSize: 1 } };
  expect(measureSemanticJsonSize(patch)).toBeLessThan(MAX_SEMANTIC_JSON_BYTES);
  expect(() => applyStylePatch(basePacket, patch)).not.toThrow();
});

// 恰好顶满 32 KiB 语义预算时应接受。
it("accepts a patch at the exact 32 KiB semantic budget limit", () => {
  // {"label":{"text":...}} 语义开销 21
  const text = "x".repeat(MAX_SEMANTIC_JSON_BYTES - 21);
  const patch = { label: { text } };
  expect(measureSemanticJsonSize(patch)).toBe(MAX_SEMANTIC_JSON_BYTES);
  expect(() => applyStylePatch(basePacket, patch)).not.toThrow();
});

// 比预算多 1 字节必须拒绝。
it("rejects a patch one byte over the semantic budget limit", () => {
  const text = "x".repeat(MAX_SEMANTIC_JSON_BYTES - 21 + 1);
  const patch = { label: { text } };
  expect(measureSemanticJsonSize(patch)).toBe(MAX_SEMANTIC_JSON_BYTES + 1);
  expect(() => applyStylePatch(basePacket, patch)).toThrow(/语义/);
});

// JSON 里的 1.0 解析后等同整数 1，应与后端「精确整数」语义一致并接受。
it("accepts rgba components that are exact integers after JSON number parsing", () => {
  // JSON 1.0 解析后与 1 无法区分；Number.isInteger(1) === true，应与后端精确整数语义一致。
  const patch = JSON.parse(
    '{"point":{"color":{"rgba":[1.0,2.0,3.0,255.0]}}}',
  ) as Record<string, unknown>;
  const result = applyStylePatch(basePacket, patch);
  expect(result.point).toMatchObject({
    color: { rgba: [1, 2, 3, 255] },
  });
});

// 嵌套深度超过 12 层应拒绝。
it("rejects nesting deeper than 12", () => {
  let nested: unknown = 1;
  for (let i = 0; i < 13; i++) {
    nested = { nested };
  }
  expect(() => applyStylePatch(basePacket, { point: nested })).toThrow();
});

// 任意数组长度超过 4096 应拒绝。
it("rejects arrays longer than 4096", () => {
  const cartesian = Array.from({ length: 4097 }, () => 1);
  expect(() =>
    applyStylePatch(basePacket, {
      polyline: { positions: { cartesian } },
    }),
  ).toThrow();
});

// patch 根必须是对象，数组根不合法。
it("rejects a non-object patch root", () => {
  expect(() =>
    applyStylePatch(basePacket, [1, 2, 3] as unknown as Record<string, unknown>),
  ).toThrow();
});

// 白名单内全部顶层视觉键均可同时出现并成功合并。
it("accepts all allowed top-level visual keys", () => {
  const result = applyStylePatch(
    { id: "entity" },
    {
      point: { pixelSize: 1 },
      path: { width: 1 },
      label: { text: "a" },
      billboard: { scale: 1 },
      model: { scale: 1 },
      polyline: { width: 1 },
      polygon: { material: {} },
      ellipse: { semiMajorAxis: 1 },
    },
  );

  expect(result.point).toEqual({ pixelSize: 1 });
  expect(result.ellipse).toEqual({ semiMajorAxis: 1 });
});

// billboard/model 内禁止非 null 的 image/gltf/uri/url（含嵌套路径），防注入外部资源。
it.each([
  { billboard: { image: "https://evil.example/a.png" } },
  { billboard: { uri: "/local/icon.png" } },
  { billboard: { url: "data:image/png;base64,xx" } },
  { model: { gltf: "https://evil.example/m.gltf" } },
  { model: { uri: "models/sat.glb" } },
  { model: { url: "https://evil.example/m.glb" } },
  { billboard: { scale: 2, nested: { image: "x.png" } } },
  { model: { scale: 1, nodeTransformations: { a: { uri: "y" } } } },
])("rejects external resource keys in billboard/model %j", (patch) => {
  expect(() => applyStylePatch(basePacket, patch)).toThrow();
});

// 外部资源键可用 null 删除；同时允许改 scale 等其它视觉字段。
it("allows null external resource keys and other visual field updates", () => {
  const result = applyStylePatch(basePacket, {
    billboard: { image: null, scale: 2 },
    model: { gltf: null, uri: null, url: null, scale: 1 },
    path: { width: 5 },
  });

  expect(result.billboard).toEqual({ scale: 2 });
  expect(result.model).toEqual({ scale: 1 });
  expect(result.path).toMatchObject({ width: 5 });
  expect(result.position).toEqual(basePacket.position);
});
