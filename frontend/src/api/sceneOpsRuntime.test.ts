import { describe, expect, it } from "vitest";
import { isSceneOp, isSceneOpArray } from "./sceneOpsRuntime";

describe("isSceneOp camera action shapes", () => {
  it("accepts well-formed camera actions", () => {
    expect(
      isSceneOp({
        op: "camera",
        action: "focus",
        targetId: "iss",
        distanceMeters: 1000,
      }),
    ).toBe(true);
    expect(isSceneOp({ op: "camera", action: "track", targetId: "iss" })).toBe(
      true,
    );
    expect(isSceneOp({ op: "camera", action: "untrack" })).toBe(true);
    expect(isSceneOp({ op: "camera", action: "zoom", amount: 10 })).toBe(true);
    expect(
      isSceneOp({
        op: "camera",
        action: "pan",
        direction: "left",
        amount: 5,
      }),
    ).toBe(true);
    expect(
      isSceneOp({
        op: "camera",
        action: "rotate",
        headingDegrees: -30,
      }),
    ).toBe(true);
    expect(
      isSceneOp({
        op: "camera",
        action: "orbitStep",
        targetId: "iss",
        amount: 45,
        distanceMeters: 1000,
      }),
    ).toBe(true);
    expect(
      isSceneOp({
        op: "camera",
        action: "orbitStart",
        targetId: "iss",
        angularSpeedDegreesPerSecond: 12,
      }),
    ).toBe(true);
    expect(isSceneOp({ op: "camera", action: "orbitStop" })).toBe(true);
  });

  it.each([
    { op: "camera", action: "focus" },
    { op: "camera", action: "focus", targetId: "" },
    { op: "camera", action: "focus", targetId: "  " },
    { op: "camera", action: "focus", targetId: "iss", distanceMeters: 0 },
    { op: "camera", action: "focus", targetId: "iss", distanceMeters: -1 },
    { op: "camera", action: "track" },
    { op: "camera", action: "track", targetId: "" },
    { op: "camera", action: "zoom" },
    { op: "camera", action: "zoom", amount: 0 },
    { op: "camera", action: "zoom", amount: Number.NaN },
    { op: "camera", action: "pan", direction: "left" },
    { op: "camera", action: "pan", direction: "sideways", amount: 1 },
    { op: "camera", action: "pan", direction: "left", amount: 0 },
    { op: "camera", action: "rotate" },
    {
      op: "camera",
      action: "rotate",
      headingDegrees: 0,
      pitchDegrees: 0,
      rollDegrees: 0,
    },
    { op: "camera", action: "rotate", headingDegrees: Number.POSITIVE_INFINITY },
    { op: "camera", action: "orbitStep", targetId: "iss" },
    { op: "camera", action: "orbitStep", targetId: "iss", amount: 0 },
    {
      op: "camera",
      action: "orbitStep",
      targetId: "iss",
      amount: 10,
      distanceMeters: 0,
    },
    { op: "camera", action: "orbitStart", targetId: "iss" },
    {
      op: "camera",
      action: "orbitStart",
      targetId: "iss",
      angularSpeedDegreesPerSecond: 0,
    },
    {
      op: "camera",
      action: "orbitStart",
      targetId: "iss",
      angularSpeedDegreesPerSecond: 1,
      distanceMeters: -5,
    },
    { op: "camera", action: "focus", targetId: "iss", headingDegrees: Number.NaN },
    { op: "camera", action: "zoom", amount: 1, direction: "diagonal" },
  ])("rejects malformed camera op %j", (op) => {
    expect(isSceneOp(op)).toBe(false);
  });

  it("allows untrack/orbitStop without targetId or amount", () => {
    expect(isSceneOp({ op: "camera", action: "untrack" })).toBe(true);
    expect(isSceneOp({ op: "camera", action: "orbitStop" })).toBe(true);
  });
});

describe("isSceneOpArray wholesale rejection", () => {
  it("rejects clear + malformed focus as a whole", () => {
    expect(
      isSceneOpArray([
        { op: "clear" },
        { op: "camera", action: "focus" },
      ]),
    ).toBe(false);
  });

  it("accepts clear + well-formed focus", () => {
    expect(
      isSceneOpArray([
        { op: "clear" },
        { op: "camera", action: "focus", targetId: "gs" },
      ]),
    ).toBe(true);
  });
});
