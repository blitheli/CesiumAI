import { expectTypeOf } from "vitest";
import type {
  ChatResponse,
  ClearSceneOp,
  DeleteSceneOp,
  SceneOp,
  UpsertSceneOp,
} from "./chat";

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

it("binds ChatResponse and SceneOp types at compile time", () => {
  expectTypeOf<ClearSceneOp>().toEqualTypeOf<{ op: "clear" }>();
  expectTypeOf<UpsertSceneOp>().toEqualTypeOf<{
    op: "upsert";
    packets: Array<{ id: string } & Record<string, unknown>>;
  }>();
  expectTypeOf<DeleteSceneOp>().toEqualTypeOf<{ op: "delete"; ids: string[] }>();
  expectTypeOf<SceneOp["op"]>().toEqualTypeOf<"clear" | "upsert" | "delete">();
  expectTypeOf<ChatResponse["sceneOps"]>().toEqualTypeOf<SceneOp[]>();
});
