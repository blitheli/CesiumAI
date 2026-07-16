import { Math as CesiumMath } from "cesium";
import { vi } from "vitest";
import type { CameraSceneOp } from "../contracts/chat";
import {
  CesiumCameraController,
  type CameraControllerPort,
  type CameraEntityAdapter,
  type CameraViewerAdapter,
} from "./CesiumCameraController";

type FakeEntity = CameraEntityAdapter & {
  positionsByTime: Map<unknown, { x: number; y: number; z: number } | undefined>;
};

function createEntity(
  id: string,
  position?: { x: number; y: number; z: number },
): FakeEntity {
  const positionsByTime = new Map<
    unknown,
    { x: number; y: number; z: number } | undefined
  >();
  if (position) {
    positionsByTime.set("t0", position);
  }

  return {
    id,
    positionsByTime,
    position: {
      getValue: (time: unknown) => {
        if (positionsByTime.has(time)) {
          return positionsByTime.get(time);
        }
        const label =
          typeof time === "object" &&
          time !== null &&
          "label" in time
            ? (time as { label: unknown }).label
            : time;
        return positionsByTime.get(label);
      },
    },
  };
}

function createAdapter(options?: {
  entities?: FakeEntity[];
  currentTime?: unknown;
  currentLookAt?: { heading: number; pitch: number; range: number };
  flyToResult?: boolean;
}): {
  adapter: CameraViewerAdapter;
  entities: Map<string, FakeEntity>;
  flyTo: ReturnType<typeof vi.fn>;
  zoomIn: ReturnType<typeof vi.fn>;
  zoomOut: ReturnType<typeof vi.fn>;
  moveLeft: ReturnType<typeof vi.fn>;
  moveRight: ReturnType<typeof vi.fn>;
  moveUp: ReturnType<typeof vi.fn>;
  moveDown: ReturnType<typeof vi.fn>;
  lookLeft: ReturnType<typeof vi.fn>;
  lookRight: ReturnType<typeof vi.fn>;
  lookUp: ReturnType<typeof vi.fn>;
  lookDown: ReturnType<typeof vi.fn>;
  twistLeft: ReturnType<typeof vi.fn>;
  twistRight: ReturnType<typeof vi.fn>;
  lookAtTransform: ReturnType<typeof vi.fn>;
  eastNorthUpToFixedFrame: ReturnType<typeof vi.fn>;
  getLookAtHeadingPitchRange: ReturnType<typeof vi.fn>;
  setTrackedEntity: ReturnType<typeof vi.fn>;
  getTrackedEntity: ReturnType<typeof vi.fn>;
  tickListeners: Array<(clock: { currentTime: unknown }) => void>;
  removeTickListener: ReturnType<typeof vi.fn>;
  setCurrentTime: (time: unknown) => void;
  setCurrentLookAt: (offset: {
    heading: number;
    pitch: number;
    range: number;
  }) => void;
  emitTick: (time: unknown, secondsOfDay?: number) => void;
} {
  const entities = new Map(
    (options?.entities ?? []).map((entity) => [entity.id, entity]),
  );
  let timeLabel: unknown = options?.currentTime ?? "t0";
  let clockSeconds = 0;
  let tracked: CameraEntityAdapter | undefined;
  let currentLookAt = options?.currentLookAt ?? {
    heading: 0,
    pitch: CesiumMath.toRadians(-45),
    range: 1000,
  };
  const tickListeners: Array<(clock: { currentTime: unknown }) => void> = [];
  const removeTickListener = vi.fn();

  const makeTime = () => ({ label: timeLabel, seconds: clockSeconds });

  const flyTo = vi.fn(async () => {
    if (options?.flyToResult === false) {
      throw new Error("相机 flyTo 未能完成。");
    }
  });
  const zoomIn = vi.fn();
  const zoomOut = vi.fn();
  const moveLeft = vi.fn();
  const moveRight = vi.fn();
  const moveUp = vi.fn();
  const moveDown = vi.fn();
  const lookLeft = vi.fn();
  const lookRight = vi.fn();
  const lookUp = vi.fn();
  const lookDown = vi.fn();
  const twistLeft = vi.fn();
  const twistRight = vi.fn();
  const lookAtTransform = vi.fn(
    (
      _transform: unknown,
      offset: { heading: number; pitch: number; range: number },
    ) => {
      currentLookAt = { ...offset };
    },
  );
  const eastNorthUpToFixedFrame = vi.fn((position) => ({
    kind: "enu",
    position,
  }));
  const getLookAtHeadingPitchRange = vi.fn(
    (_position: { x: number; y: number; z: number }) => ({
      ...currentLookAt,
    }),
  );
  const setTrackedEntity = vi.fn((entity?: CameraEntityAdapter) => {
    tracked = entity;
  });
  const getTrackedEntity = vi.fn(() => tracked);

  const adapter: CameraViewerAdapter = {
    flyTo,
    getTrackedEntity,
    setTrackedEntity,
    zoomIn,
    zoomOut,
    moveLeft,
    moveRight,
    moveUp,
    moveDown,
    lookLeft,
    lookRight,
    lookUp,
    lookDown,
    twistLeft,
    twistRight,
    getCurrentTime: () => makeTime(),
    secondsDifference: (later, earlier) => {
      const laterSeconds = Number((later as { seconds: number }).seconds);
      const earlierSeconds = Number((earlier as { seconds: number }).seconds);
      return laterSeconds - earlierSeconds;
    },
    cloneTime: (time) => {
      const value = time as { label: unknown; seconds: number };
      return { label: value.label, seconds: value.seconds };
    },
    addTickListener: (listener) => {
      tickListeners.push(listener);
      return () => {
        removeTickListener();
        const index = tickListeners.indexOf(listener);
        if (index >= 0) {
          tickListeners.splice(index, 1);
        }
      };
    },
    lookAtTransform,
    eastNorthUpToFixedFrame,
    getLookAtHeadingPitchRange,
    getEntityById: (id) => entities.get(id),
  };

  return {
    adapter,
    entities,
    flyTo,
    zoomIn,
    zoomOut,
    moveLeft,
    moveRight,
    moveUp,
    moveDown,
    lookLeft,
    lookRight,
    lookUp,
    lookDown,
    twistLeft,
    twistRight,
    lookAtTransform,
    eastNorthUpToFixedFrame,
    getLookAtHeadingPitchRange,
    setTrackedEntity,
    getTrackedEntity,
    tickListeners,
    removeTickListener,
    setCurrentTime: (time) => {
      timeLabel = time;
    },
    setCurrentLookAt: (offset) => {
      currentLookAt = { ...offset };
    },
    emitTick: (time, nextSeconds = clockSeconds + 1) => {
      timeLabel = time;
      clockSeconds = nextSeconds;
      const clock = { currentTime: makeTime() };
      for (const listener of [...tickListeners]) {
        listener(clock);
      }
    },
  };
}

function cameraOp(partial: Omit<CameraSceneOp, "op">): CameraSceneOp {
  return { op: "camera", ...partial };
}

function createController(adapter: CameraViewerAdapter): CameraControllerPort {
  return new CesiumCameraController(adapter);
}

it("focus 查找目标实体并 flyTo，角度转为弧度", async () => {
  const entity = createEntity("iss", { x: 1, y: 2, z: 3 });
  const fake = createAdapter({ entities: [entity] });
  const controller = createController(fake.adapter);

  await controller.apply(
    cameraOp({
      action: "focus",
      targetId: "iss",
      distanceMeters: 1200,
      headingDegrees: 90,
      pitchDegrees: -30,
    }),
  );

  expect(fake.flyTo).toHaveBeenCalledOnce();
  expect(fake.flyTo).toHaveBeenCalledWith(entity, {
    offset: {
      heading: CesiumMath.toRadians(90),
      pitch: CesiumMath.toRadians(-30),
      range: 1200,
    },
  });
});

it("track/untrack 设置与清除 trackedEntity", async () => {
  const entity = createEntity("iss", { x: 1, y: 2, z: 3 });
  const fake = createAdapter({ entities: [entity] });
  const controller = createController(fake.adapter);

  await controller.apply(cameraOp({ action: "track", targetId: "iss" }));
  expect(fake.setTrackedEntity).toHaveBeenCalledWith(entity);

  await controller.apply(cameraOp({ action: "untrack" }));
  expect(fake.setTrackedEntity).toHaveBeenCalledWith(undefined);
});

it("zoom 按正负 amount 调用 zoomIn/zoomOut", async () => {
  const fake = createAdapter();
  const controller = createController(fake.adapter);

  await controller.apply(cameraOp({ action: "zoom", amount: 250 }));
  expect(fake.zoomIn).toHaveBeenCalledWith(250);

  await controller.apply(cameraOp({ action: "zoom", amount: -100 }));
  expect(fake.zoomOut).toHaveBeenCalledWith(100);
});

it("pan 按方向调用相对平移 API", async () => {
  const fake = createAdapter();
  const controller = createController(fake.adapter);

  await controller.apply(
    cameraOp({ action: "pan", direction: "left", amount: 40 }),
  );
  expect(fake.moveLeft).toHaveBeenCalledWith(40);

  await controller.apply(
    cameraOp({ action: "pan", direction: "right", amount: 10 }),
  );
  expect(fake.moveRight).toHaveBeenCalledWith(10);

  await controller.apply(
    cameraOp({ action: "pan", direction: "up", amount: 5 }),
  );
  expect(fake.moveUp).toHaveBeenCalledWith(5);

  await controller.apply(
    cameraOp({ action: "pan", direction: "down", amount: 7 }),
  );
  expect(fake.moveDown).toHaveBeenCalledWith(7);
});

it("rotate 将相对角度转为弧度并调用 look/twist", async () => {
  const fake = createAdapter();
  const controller = createController(fake.adapter);

  await controller.apply(
    cameraOp({
      action: "rotate",
      headingDegrees: 15,
      pitchDegrees: -8,
      rollDegrees: 3,
    }),
  );

  expect(fake.lookRight).toHaveBeenCalledWith(CesiumMath.toRadians(15));
  expect(fake.lookDown).toHaveBeenCalledWith(CesiumMath.toRadians(8));
  expect(fake.twistRight).toHaveBeenCalledWith(CesiumMath.toRadians(3));

  await controller.apply(
    cameraOp({
      action: "rotate",
      headingDegrees: -12,
      pitchDegrees: 6,
      rollDegrees: -4,
    }),
  );

  expect(fake.lookLeft).toHaveBeenCalledWith(CesiumMath.toRadians(12));
  expect(fake.lookUp).toHaveBeenCalledWith(CesiumMath.toRadians(6));
  expect(fake.twistLeft).toHaveBeenCalledWith(CesiumMath.toRadians(4));
});

it("orbitStep 基于当前相对目标 heading 做增量，而非绝对 0", async () => {
  const entity = createEntity("iss");
  entity.position = {
    getValue: () => ({ x: 10, y: 20, z: 30 }),
  };
  const fake = createAdapter({
    entities: [entity],
    currentTime: "t0",
    currentLookAt: {
      heading: CesiumMath.toRadians(30),
      pitch: CesiumMath.toRadians(-20),
      range: 800,
    },
  });
  const controller = createController(fake.adapter);

  await controller.apply(
    cameraOp({
      action: "orbitStep",
      targetId: "iss",
      amount: 45,
    }),
  );

  expect(fake.getLookAtHeadingPitchRange).toHaveBeenCalledWith({
    x: 10,
    y: 20,
    z: 30,
  });
  expect(fake.lookAtTransform).toHaveBeenCalledOnce();
  const [, offset] = fake.lookAtTransform.mock.calls[0]!;
  expect(fake.lookAtTransform.mock.calls[0]![0]).toEqual({
    kind: "enu",
    position: { x: 10, y: 20, z: 30 },
  });
  expect(offset.heading).toBeCloseTo(CesiumMath.toRadians(30 + 45), 10);
  expect(offset.pitch).toBeCloseTo(CesiumMath.toRadians(-20), 10);
  expect(offset.range).toBeCloseTo(800, 10);
  expect(fake.tickListeners).toHaveLength(0);
});

it("orbitStep 传入显式非零 headingDegrees 时以其为基准再加 amount", async () => {
  const entity = createEntity("iss");
  entity.position = {
    getValue: () => ({ x: 10, y: 20, z: 30 }),
  };
  const fake = createAdapter({
    entities: [entity],
    currentTime: "t0",
    // 当前视角 heading=30°，但显式基准应覆盖为 10°
    currentLookAt: {
      heading: CesiumMath.toRadians(30),
      pitch: CesiumMath.toRadians(-20),
      range: 800,
    },
  });
  const controller = createController(fake.adapter);

  await controller.apply(
    cameraOp({
      action: "orbitStep",
      targetId: "iss",
      headingDegrees: 10,
      amount: 45,
      pitchDegrees: -15,
      distanceMeters: 600,
    }),
  );

  const [, offset] = fake.lookAtTransform.mock.calls[0]!;
  // targetHeading = 10 + 45；pitch/distance 可选覆盖
  expect(offset.heading).toBeCloseTo(CesiumMath.toRadians(10 + 45), 10);
  expect(offset.pitch).toBeCloseTo(CesiumMath.toRadians(-15), 10);
  expect(offset.range).toBeCloseTo(600, 10);
});

it("pan 只接受正距离；zoom 明确用符号区分拉近/拉远", async () => {
  const fake = createAdapter();
  const controller = createController(fake.adapter);

  await expect(
    controller.apply(
      cameraOp({ action: "pan", direction: "left", amount: 0 }),
    ),
  ).rejects.toThrow(/正|positive|> 0/i);
  await expect(
    controller.apply(
      cameraOp({ action: "pan", direction: "left", amount: -10 }),
    ),
  ).rejects.toThrow(/正|positive|> 0/i);
  expect(fake.moveLeft).not.toHaveBeenCalled();

  await controller.apply(
    cameraOp({ action: "pan", direction: "left", amount: 40 }),
  );
  expect(fake.moveLeft).toHaveBeenCalledWith(40);

  await controller.apply(cameraOp({ action: "zoom", amount: 250 }));
  expect(fake.zoomIn).toHaveBeenCalledWith(250);
  await controller.apply(cameraOp({ action: "zoom", amount: -100 }));
  expect(fake.zoomOut).toHaveBeenCalledWith(100);
});

it("focus 在 flyTo 失败时抛错", async () => {
  const entity = createEntity("iss", { x: 1, y: 2, z: 3 });
  const fake = createAdapter({ entities: [entity], flyToResult: false });
  const controller = createController(fake.adapter);

  await expect(
    controller.apply(cameraOp({ action: "focus", targetId: "iss" })),
  ).rejects.toThrow(/flyTo|未能完成/i);
});

it("track/orbitStep/orbitStart 校验失败时保留原有 track/orbit 状态", async () => {
  const entity = createEntity("iss", { x: 1, y: 2, z: 3 });
  const ghost = createEntity("ghost");
  const fake = createAdapter({ entities: [entity, ghost] });
  const controller = createController(fake.adapter);

  await controller.apply(
    cameraOp({
      action: "orbitStart",
      targetId: "iss",
      angularSpeedDegreesPerSecond: 20,
      distanceMeters: 300,
    }),
  );
  expect(fake.tickListeners).toHaveLength(1);
  fake.removeTickListener.mockClear();
  fake.setTrackedEntity.mockClear();

  await expect(
    controller.apply(cameraOp({ action: "track", targetId: "missing" })),
  ).rejects.toThrow(/不存在|目标/i);
  expect(fake.tickListeners).toHaveLength(1);
  expect(fake.removeTickListener).not.toHaveBeenCalled();

  await expect(
    controller.apply(
      cameraOp({
        action: "orbitStart",
        targetId: "ghost",
        angularSpeedDegreesPerSecond: 10,
        distanceMeters: 200,
      }),
    ),
  ).rejects.toThrow(/位置|position/i);
  expect(fake.tickListeners).toHaveLength(1);
  expect(fake.removeTickListener).not.toHaveBeenCalled();

  await controller.apply(cameraOp({ action: "orbitStop" }));
  await controller.apply(cameraOp({ action: "track", targetId: "iss" }));
  fake.setTrackedEntity.mockClear();

  await expect(
    controller.apply(
      cameraOp({
        action: "orbitStep",
        targetId: "ghost",
        amount: 15,
      }),
    ),
  ).rejects.toThrow(/位置|position/i);
  expect(fake.adapter.getTrackedEntity()).toBe(entity);
  expect(fake.setTrackedEntity).not.toHaveBeenCalledWith(undefined);
});

it("orbitStart 只注册一个 tick listener，每 tick 更新动态中心与 heading", async () => {
  const entity = createEntity("iss", { x: 1, y: 1, z: 1 });
  entity.position = {
    getValue: (time: unknown) => {
      const label = (time as { label?: unknown }).label ?? time;
      if (label === "t1") {
        return { x: 2, y: 3, z: 4 };
      }
      if (label === "t2") {
        return { x: 5, y: 6, z: 7 };
      }
      return { x: 1, y: 1, z: 1 };
    },
  };
  const fake = createAdapter({ entities: [entity], currentTime: "t0" });
  const controller = createController(fake.adapter);

  await controller.apply(
    cameraOp({
      action: "orbitStart",
      targetId: "iss",
      angularSpeedDegreesPerSecond: 90,
      headingDegrees: 0,
      pitchDegrees: -45,
      distanceMeters: 1000,
    }),
  );

  expect(fake.tickListeners).toHaveLength(1);
  expect(fake.lookAtTransform).toHaveBeenCalledTimes(1);

  fake.emitTick("t1", 1);
  expect(fake.eastNorthUpToFixedFrame).toHaveBeenLastCalledWith({
    x: 2,
    y: 3,
    z: 4,
  });
  expect(fake.lookAtTransform).toHaveBeenLastCalledWith(
    { kind: "enu", position: { x: 2, y: 3, z: 4 } },
    {
      heading: CesiumMath.toRadians(90),
      pitch: CesiumMath.toRadians(-45),
      range: 1000,
    },
  );

  fake.emitTick("t2", 2);
  expect(fake.lookAtTransform).toHaveBeenLastCalledWith(
    { kind: "enu", position: { x: 5, y: 6, z: 7 } },
    {
      heading: CesiumMath.toRadians(180),
      pitch: CesiumMath.toRadians(-45),
      range: 1000,
    },
  );
  expect(fake.tickListeners).toHaveLength(1);
});

it("track 与 orbit 互斥：track 停止环绕，orbit 清除跟随", async () => {
  const entity = createEntity("iss", { x: 1, y: 2, z: 3 });
  const fake = createAdapter({ entities: [entity] });
  const controller = createController(fake.adapter);

  await controller.apply(
    cameraOp({
      action: "orbitStart",
      targetId: "iss",
      angularSpeedDegreesPerSecond: 30,
      distanceMeters: 500,
    }),
  );
  expect(fake.tickListeners).toHaveLength(1);

  await controller.apply(cameraOp({ action: "track", targetId: "iss" }));
  expect(fake.removeTickListener).toHaveBeenCalled();
  expect(fake.tickListeners).toHaveLength(0);
  expect(fake.setTrackedEntity).toHaveBeenCalledWith(entity);

  await controller.apply(
    cameraOp({
      action: "orbitStart",
      targetId: "iss",
      angularSpeedDegreesPerSecond: 10,
      distanceMeters: 400,
    }),
  );
  expect(fake.setTrackedEntity).toHaveBeenCalledWith(undefined);
  expect(fake.tickListeners).toHaveLength(1);
});

it("orbitStop、clear、目标删除和 destroy 都移除 tick listener", async () => {
  const entity = createEntity("iss", { x: 1, y: 2, z: 3 });
  const fake = createAdapter({ entities: [entity] });
  const controller = createController(fake.adapter);

  await controller.apply(
    cameraOp({
      action: "orbitStart",
      targetId: "iss",
      angularSpeedDegreesPerSecond: 20,
      distanceMeters: 300,
    }),
  );
  await controller.apply(cameraOp({ action: "orbitStop" }));
  expect(fake.tickListeners).toHaveLength(0);

  await controller.apply(
    cameraOp({
      action: "orbitStart",
      targetId: "iss",
      angularSpeedDegreesPerSecond: 20,
      distanceMeters: 300,
    }),
  );
  controller.onSceneCleared();
  expect(fake.tickListeners).toHaveLength(0);
  expect(fake.setTrackedEntity).toHaveBeenCalledWith(undefined);

  await controller.apply(
    cameraOp({
      action: "orbitStart",
      targetId: "iss",
      angularSpeedDegreesPerSecond: 20,
      distanceMeters: 300,
    }),
  );
  controller.onEntitiesDeleted(["iss"]);
  expect(fake.tickListeners).toHaveLength(0);

  await controller.apply(
    cameraOp({
      action: "orbitStart",
      targetId: "iss",
      angularSpeedDegreesPerSecond: 20,
      distanceMeters: 300,
    }),
  );
  controller.destroy();
  expect(fake.tickListeners).toHaveLength(0);
});

it("目标不存在或当前时间无 position 时抛错且无 listener 泄漏", async () => {
  const noPosition = createEntity("ghost");
  const fake = createAdapter({ entities: [noPosition], currentTime: "t0" });
  const controller = createController(fake.adapter);

  await expect(
    controller.apply(cameraOp({ action: "focus", targetId: "missing" })),
  ).rejects.toThrow(/不存在|missing|目标/i);
  expect(fake.tickListeners).toHaveLength(0);

  await expect(
    controller.apply(
      cameraOp({
        action: "orbitStart",
        targetId: "ghost",
        angularSpeedDegreesPerSecond: 15,
        distanceMeters: 200,
      }),
    ),
  ).rejects.toThrow(/位置|position|无效/i);
  expect(fake.tickListeners).toHaveLength(0);
  expect(fake.removeTickListener).not.toHaveBeenCalled();
});
