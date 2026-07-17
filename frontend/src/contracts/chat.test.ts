import { expectTypeOf } from "vitest";
import type {
  CameraSceneOp,
  ChatResponse,
  ClearSceneOp,
  DeleteSceneOp,
  SceneOp,
  StyleSceneOp,
  UpsertSceneOp,
} from "./chat";

it("accepts the wire-level SceneOp union", () => {
  const operations: SceneOp[] = [
    { op: "clear" },
    { op: "upsert", packets: [{ id: "sanya" }] },
    { op: "delete", ids: ["old"] },
    { op: "camera", action: "track", targetId: "iss" },
    { op: "style", id: "iss", patch: { path: { width: 5 } } },
  ];

  const response = {
    sessionId: "s1",
    message: "done",
    sceneOps: operations,
  } satisfies ChatResponse;

  expect(response.sceneOps.map((op) => op.op)).toEqual([
    "clear",
    "upsert",
    "delete",
    "camera",
    "style",
  ]);
});

it("binds ChatResponse and SceneOp types at compile time", () => {
  expectTypeOf<ClearSceneOp>().toEqualTypeOf<{ op: "clear" }>();
  expectTypeOf<UpsertSceneOp>().toEqualTypeOf<{
    op: "upsert";
    packets: Array<{ id: string } & Record<string, unknown>>;
  }>();
  expectTypeOf<DeleteSceneOp>().toEqualTypeOf<{ op: "delete"; ids: string[] }>();
  expectTypeOf<CameraSceneOp>().toMatchTypeOf<{
    op: "camera";
    action:
      | "focus"
      | "track"
      | "untrack"
      | "zoom"
      | "pan"
      | "rotate"
      | "orbitStep"
      | "orbitStart"
      | "orbitStop";
    targetId?: string | null;
    distanceMeters?: number | null;
    direction?: "left" | "right" | "up" | "down" | null;
  }>();
  expectTypeOf<StyleSceneOp>().toEqualTypeOf<{
    op: "style";
    id: string;
    patch: Record<string, unknown>;
  }>();
  expectTypeOf<SceneOp["op"]>().toEqualTypeOf<
    "clear" | "upsert" | "delete" | "camera" | "style"
  >();
  expectTypeOf<ChatResponse["sceneOps"]>().toEqualTypeOf<SceneOp[]>();
});

it("mirrors backend camera SceneOp camelCase wire fields", () => {
  const operation = {
    op: "camera",
    action: "focus",
    targetId: "iss",
    distanceMeters: 2_000_000,
    headingDegrees: 15,
    pitchDegrees: -30,
    rollDegrees: 2,
    amount: 45,
    direction: "left",
    angularSpeedDegreesPerSecond: 12,
  } satisfies CameraSceneOp;

  expect(operation).toMatchObject({
    op: "camera",
    action: "focus",
    targetId: "iss",
    distanceMeters: 2_000_000,
    angularSpeedDegreesPerSecond: 12,
  });
});

it("accepts explicit nulls on optional camera wire fields", () => {
  const operation = {
    op: "camera",
    action: "track",
    targetId: null,
    distanceMeters: null,
    headingDegrees: null,
    pitchDegrees: null,
    rollDegrees: null,
    amount: null,
    direction: null,
    angularSpeedDegreesPerSecond: null,
  } satisfies CameraSceneOp;

  expectTypeOf<CameraSceneOp["targetId"]>().toEqualTypeOf<
    string | null | undefined
  >();
  expectTypeOf<CameraSceneOp["distanceMeters"]>().toEqualTypeOf<
    number | null | undefined
  >();
  expectTypeOf<CameraSceneOp["headingDegrees"]>().toEqualTypeOf<
    number | null | undefined
  >();
  expectTypeOf<CameraSceneOp["pitchDegrees"]>().toEqualTypeOf<
    number | null | undefined
  >();
  expectTypeOf<CameraSceneOp["rollDegrees"]>().toEqualTypeOf<
    number | null | undefined
  >();
  expectTypeOf<CameraSceneOp["amount"]>().toEqualTypeOf<
    number | null | undefined
  >();
  expectTypeOf<CameraSceneOp["direction"]>().toEqualTypeOf<
    "left" | "right" | "up" | "down" | null | undefined
  >();
  expectTypeOf<CameraSceneOp["angularSpeedDegreesPerSecond"]>().toEqualTypeOf<
    number | null | undefined
  >();
  expect(operation.targetId).toBeNull();
  expect(operation.angularSpeedDegreesPerSecond).toBeNull();
});
