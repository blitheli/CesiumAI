import { createEmptyDocument } from "./emptyDocument";
import { reduceSceneDocument } from "./sceneDocument";

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
      { op: "upsert", packets: [{ id: "sanya", name: "new", point: { pixelSize: 10 } }] },
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

it("does not mutate caller-owned arrays or packet objects", () => {
  const empty = createEmptyDocument(new Date("2026-07-16T00:00:00Z"));
  const sanya = { id: "sanya", name: "old", point: { pixelSize: 4 } };
  const current = [...empty, sanya];
  const operations = [
    { op: "upsert" as const, packets: [{ id: "sanya", name: "new", point: { pixelSize: 10 } }] },
  ];

  const result = reduceSceneDocument(current, operations, empty);

  expect(current).toHaveLength(2);
  expect(sanya.name).toBe("old");
  expect(sanya.point).toEqual({ pixelSize: 4 });
  expect(result).not.toBe(current);
  expect(result.find((packet) => packet.id === "sanya")).not.toBe(sanya);
});

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
