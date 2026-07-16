import type { ChatResponse } from "./chat";

it("accepts the wire-level SceneOp union", () => {
  const response = {
    sessionId: "s1",
    message: "done",
    sceneOps: [
      { op: "clear" },
      { op: "upsert", packets: [{ id: "sanya" }] },
      { op: "delete", ids: ["old"] },
    ],
  } satisfies ChatResponse;

  expect(response.sceneOps.map((op) => op.op)).toEqual([
    "clear",
    "upsert",
    "delete",
  ]);
});
