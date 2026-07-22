import { createEmptyDocument } from "./emptyDocument";
import {
  buildSceneSummary,
  inferRelevantEntityIds,
  pickRelevantPackets,
} from "./summary";

// 有 point + cartographicDegrees → 归类为 facility，并带上 lon/lat/alt。
it("classifies a static facility from point and cartographicDegrees", () => {
  const document = [
    ...createEmptyDocument(new Date("2026-07-16T00:00:00Z")),
    {
      id: "sanya",
      name: "Sanya Ground Station",
      point: { pixelSize: 8 },
      position: { cartographicDegrees: [109.5, 18.2, 50] },
    },
  ];

  const summary = buildSceneSummary(document);

  expect(summary.entities).toEqual([
    {
      id: "sanya",
      name: "Sanya Ground Station",
      type: "facility",
      lon: 109.5,
      lat: 18.2,
      alt: 50,
    },
  ]);
});

// 有 path → 归类为 satellite；无 properties.orbitHint 时 orbitHint 回退为 name。
it("classifies a satellite from path and cartesianVelocity", () => {
  const document = [
    ...createEmptyDocument(new Date("2026-07-16T00:00:00Z")),
    {
      id: "sat-1",
      name: "SSO-900",
      path: { width: 1 },
      position: { cartesianVelocity: [1, 0, 0, 0, 1, 0] },
    },
  ];

  const summary = buildSceneSummary(document);

  expect(summary.entities).toEqual([
    {
      id: "sat-1",
      name: "SSO-900",
      type: "satellite",
      orbitHint: "SSO-900",
    },
  ]);
});

// 存在 properties.orbitHint.string 时优先用其作为 orbitHint，而不是 name。
it("uses properties.orbitHint.string for satellite orbitHint when present", () => {
  const document = [
    ...createEmptyDocument(new Date("2026-07-16T00:00:00Z")),
    {
      id: "sat-2",
      name: "Fallback Name",
      path: {},
      position: { cartesianVelocity: [0, 0, 0, 0, 0, 0] },
      properties: { orbitHint: { string: "Sun-sync 900km" } },
    },
  ];

  const summary = buildSceneSummary(document);

  expect(summary.entities[0]?.orbitHint).toBe("Sun-sync 900km");
});

// document 包不进 entities，但其 clock 会进入 documentClock。
it("excludes document packets from the summary", () => {
  const document = createEmptyDocument(new Date("2026-07-16T00:00:00Z"));

  const summary = buildSceneSummary(document);

  expect(summary.entities).toEqual([]);
  expect(summary.documentClock).toEqual({
    interval: "2026-07-16T00:00:00.000Z/2026-07-17T00:00:00.000Z",
    currentTime: "2026-07-16T00:00:00.000Z",
  });
});

// 相关 id：选中优先；再按文本大小写不敏感匹配 id/name；去重且保持顺序。
it("returns selected ids first, then case-insensitive id/name substring matches without duplicates", () => {
  const summary = {
    entities: [
      { id: "sanya", name: "Sanya Ground Station", type: "facility" as const },
      { id: "beijing", name: "Beijing Hub", type: "facility" as const },
      { id: "sat-1", name: "SSO-900", type: "satellite" as const },
    ],
  };

  const ids = inferRelevantEntityIds(
    "please adjust SSO-900 and sanya height",
    summary,
    ["beijing"],
  );

  expect(ids).toEqual(["beijing", "sanya", "sat-1"]);
});

// 只返回请求 id 对应的完整 packet 深拷贝；缺失 id 跳过，且不共享原对象引用。
it("pickRelevantPackets returns cloned full packets only for requested ids", () => {
  const sanya = {
    id: "sanya",
    name: "Sanya",
    point: { pixelSize: 8 },
    position: { cartographicDegrees: [109.5, 18.2, 50] },
  };
  const beijing = { id: "beijing", point: {} };
  const document = [
    ...createEmptyDocument(new Date("2026-07-16T00:00:00Z")),
    sanya,
    beijing,
  ];

  const picked = pickRelevantPackets(document, ["sanya", "missing"]);

  expect(picked).toHaveLength(1);
  expect(picked[0]).toEqual(sanya);
  expect(picked[0]).not.toBe(sanya);
});
