import type { CzmlPacket } from "../contracts/chat";
import { createEmptyDocument } from "./emptyDocument";
import { reduceSceneDocument } from "./sceneDocument";

// 同一批 ops 内：upsert 按 id 整包替换，再 delete 指定实体；二者按数组顺序生效。
it("clears, replaces complete packets by id, and deletes in order", () => {
  const empty = createEmptyDocument(new Date("2026-07-16T00:00:00Z"));
  const current = [
    ...empty,
    { id: "sanya", name: "old", point: { pixelSize: 4 } },
    { id: "remove-me", point: {} },
  ];
  const result = reduceSceneDocument(
    current,
    [
      {
        op: "upsert",
        packets: [{ id: "sanya", name: "new", point: { pixelSize: 10 } }],
      },
      { op: "delete", ids: ["remove-me"] },
    ],
    empty,
  );

  expect(result.find((packet) => packet.id === "sanya")).toEqual({
    id: "sanya",
    name: "new",
    point: { pixelSize: 10 },
  });
  expect(result.some((packet) => packet.id === "remove-me")).toBe(false);
});

// clear：丢弃全部业务实体，结果只剩 emptyDocument 中的 document packet。
it("clear discards every business entity and keeps only the empty document", () => {
  const empty = createEmptyDocument(new Date("2026-07-16T00:00:00Z"));
  const current = [
    ...empty,
    { id: "sanya", point: {} },
    { id: "beijing", point: {} },
  ];
  const result = reduceSceneDocument(current, [{ op: "clear" }], empty);

  expect(result).toHaveLength(1);
  expect(result[0]?.id).toBe("document");
  expect(result.some((packet) => packet.id === "sanya")).toBe(false);
  expect(result.some((packet) => packet.id === "beijing")).toBe(false);
});

// 归约必须深拷贝：调用方持有的 current 数组与 packet 对象不得被原地修改。
it("does not mutate caller-owned arrays or packet objects", () => {
  const empty = createEmptyDocument(new Date("2026-07-16T00:00:00Z"));
  const sanya = { id: "sanya", name: "old", point: { pixelSize: 4 } };
  const current = [...empty, sanya];
  const operations = [
    {
      op: "upsert" as const,
      packets: [{ id: "sanya", name: "new", point: { pixelSize: 10 } }],
    },
  ];

  const result = reduceSceneDocument(current, operations, empty);

  expect(current).toHaveLength(2);
  expect(sanya.name).toBe("old");
  expect(sanya.point).toEqual({ pixelSize: 4 });
  expect(result).not.toBe(current);
  expect(result.find((packet) => packet.id === "sanya")).not.toBe(sanya);
});

it("sytle,更改sanya的坐标", () => {
  const empty = createEmptyDocument(new Date("2026-07-16T00:00:00Z"));
  const sanya = {
    id: "sanya",
    name: "sanya",
    point: { pixelSize: 4 },
    position: {
      cartographicDegrees: [100, 20, 30],
    },
  };

  const current = [...empty, sanya];
  const operations = [
    {
      op: "style" as const,
      id: "sanya", 
      patch: { position: { cartographicDegrees: [90, 10, 10] } }
    },
  ];
    
  expect(() => reduceSceneDocument(current, operations, empty))
    .toThrow("不允许的样式顶层属性：'position'。")
});

// document 由前端权威维护：业务侧 upsert id=document 应被忽略，不能改名。
it("rejects upsert of document packets", () => {
  const empty = createEmptyDocument(new Date("2026-07-16T00:00:00Z"));
  const result = reduceSceneDocument(
    empty,
    [{ op: "upsert", packets: [{ id: "document", name: "Hacked" }] }],
    empty,
  );

  expect(result).toHaveLength(1);
  expect(result[0]?.name).toBe("CesiumAI Scene");
});

// delete 列表含 document 时仍保留 document packet，场景时钟根节点不可删。
it("ignores delete of document id", () => {
  const empty = createEmptyDocument(new Date("2026-07-16T00:00:00Z"));
  const result = reduceSceneDocument(
    empty,
    [{ op: "delete", ids: ["document"] }],
    empty,
  );

  expect(result).toHaveLength(1);
  expect(result[0]?.id).toBe("document");
});

// upsert 的 packet.id 缺失、空串或纯空白时必须抛错，防止产生无 id 实体。
it("throws on upsert packets with missing or blank id", () => {
  const empty = createEmptyDocument(new Date("2026-07-16T00:00:00Z"));

  expect(() =>
    reduceSceneDocument(
      empty,
      [{ op: "upsert", packets: [{} as unknown as CzmlPacket] }],
      empty,
    ),
  ).toThrow("Upsert packet id must be a non-empty string");

  expect(() =>
    reduceSceneDocument(
      empty,
      [{ op: "upsert", packets: [{ id: "" } as unknown as CzmlPacket] }],
      empty,
    ),
  ).toThrow("Upsert packet id must be a non-empty string");

  expect(() =>
    reduceSceneDocument(
      empty,
      [{ op: "upsert", packets: [{ id: "   " } as unknown as CzmlPacket] }],
      empty,
    ),
  ).toThrow("Upsert packet id must be a non-empty string");
});

// ops 严格按数组顺序执行：clear 之前的 upsert 会被清掉，clear 之后的 upsert 再被 delete。
it("executes upsert, clear, upsert, and delete in array order", () => {
  const empty = createEmptyDocument(new Date("2026-07-16T00:00:00Z"));
  const current = [...empty, { id: "before-clear", point: {} }];
  const result = reduceSceneDocument(
    current,
    [
      {
        op: "upsert",
        packets: [{ id: "staged", name: "cleared away", point: {} }],
      },
      { op: "clear" },
      {
        op: "upsert",
        packets: [{ id: "after-clear", name: "then removed", point: {} }],
      },
      { op: "delete", ids: ["after-clear"] },
    ],
    empty,
  );

  expect(result.some((packet) => packet.id === "before-clear")).toBe(false);
  expect(result.some((packet) => packet.id === "staged")).toBe(false);
  expect(result.some((packet) => packet.id === "after-clear")).toBe(false);
  expect(result).toHaveLength(1);
  expect(result[0]?.id).toBe("document");
});

// style 深合并视觉字段（path/point），同时保留 position / availability / properties 等动态数据。
it("applies style patches with deep merge while preserving dynamic position data", () => {
  const empty = createEmptyDocument(new Date("2026-07-16T00:00:00Z"));
  const position = {
    epoch: "2026-07-16T00:00:00Z",
    cartesian: [0, 1, 2, 3, 4, 5, 6],
  };
  const current = [
    ...empty,
    {
      id: "iss",
      availability: "2026-07-16T00:00:00Z/2026-07-17T00:00:00Z",
      position,
      properties: { orbitHint: "sgp4" },
      path: { width: 2, show: true },
      point: { pixelSize: 8 },
    },
  ];

  const result = reduceSceneDocument(
    current,
    [
      {
        op: "style",
        id: "iss",
        patch: { path: { width: 5 }, point: { pixelSize: 12 } },
      },
    ],
    empty,
  );

  const iss = result.find((packet) => packet.id === "iss");
  expect(iss?.position).toEqual(position);
  expect(iss?.availability).toBe("2026-07-16T00:00:00Z/2026-07-17T00:00:00Z");
  expect(iss?.properties).toEqual({ orbitHint: "sgp4" });
  expect(iss?.path).toEqual({ width: 5, show: true });
  expect(iss?.point).toEqual({ pixelSize: 12 });
});

// style 中 null 表示删除该视觉字段；数组（如 rgba）整体替换而非按元素合并。
it("replaces arrays and deletes null visual fields during style reduction", () => {
  const empty = createEmptyDocument(new Date("2026-07-16T00:00:00Z"));
  const current = [
    ...empty,
    {
      id: "iss",
      position: { cartesian: [1, 2, 3] },
      path: { width: 2 },
      point: { color: { rgba: [0, 255, 0, 255] } },
      label: { text: "old" },
    },
  ];

  const result = reduceSceneDocument(
    current,
    [
      {
        op: "style",
        id: "iss",
        patch: {
          label: null,
          point: { color: { rgba: [255, 0, 0, 255] } },
        },
      },
    ],
    empty,
  );

  const iss = result.find((packet) => packet.id === "iss");
  expect(iss?.label).toBeUndefined();
  expect(iss?.point).toEqual({ color: { rgba: [255, 0, 0, 255] } });
  expect(iss?.position).toEqual({ cartesian: [1, 2, 3] });
});

// style 非法场景：不能改 document、目标必须存在、禁止改 position / 未知顶层键。
it("rejects style on document, missing entities, and illegal patches", () => {
  const empty = createEmptyDocument(new Date("2026-07-16T00:00:00Z"));
  const current = [...empty, { id: "iss", path: { width: 2 } }];

  expect(() =>
    reduceSceneDocument(
      current,
      [{ op: "style", id: "document", patch: { path: { width: 5 } } }],
      empty,
    ),
  ).toThrow();

  expect(() =>
    reduceSceneDocument(
      current,
      [{ op: "style", id: "missing", patch: { path: { width: 5 } } }],
      empty,
    ),
  ).toThrow();

  expect(() =>
    reduceSceneDocument(
      current,
      [{ op: "style", id: "iss", patch: { position: {} } }],
      empty,
    ),
  ).toThrow();

  expect(() =>
    reduceSceneDocument(
      current,
      [{ op: "style", id: "iss", patch: { unknown: {} } }],
      empty,
    ),
  ).toThrow();
});

// camera 不参与文档归约：本层抛错且不改动入参文档（应由 SceneManager 路由到相机控制器）。
it("throws unsupported for camera operations until camera control is wired", () => {
  const empty = createEmptyDocument(new Date("2026-07-16T00:00:00Z"));
  const current = [...empty, { id: "iss", path: { width: 2 } }];

  expect(() =>
    reduceSceneDocument(
      current,
      [{ op: "camera", action: "track", targetId: "iss" }],
      empty,
    ),
  ).toThrow(/unsupported|未支持|相机/i);

  expect(current.find((packet) => packet.id === "iss")).toEqual({
    id: "iss",
    path: { width: 2 },
  });
});
