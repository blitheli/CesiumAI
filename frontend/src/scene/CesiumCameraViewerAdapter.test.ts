import {
  Cartesian3,
  HeadingPitchRange,
  JulianDate,
  Math as CesiumMath,
  Matrix4,
  ScreenSpaceEventType,
  Transforms,
} from "cesium";
import { vi } from "vitest";
import {
  createCesiumCameraViewerAdapter,
  createCesiumOrbitUserInputAdapter,
  headingPitchRangeFromLocalOffset,
  localOffsetFromHeadingPitchRange,
} from "./CesiumCameraController";

type TickListener = (clock: { currentTime: JulianDate }) => void;

function createViewerLikeFake(options?: { flyToResult?: boolean | Promise<boolean> }) {
  const tickListeners = new Set<TickListener>();
  const removeTick = vi.fn();
  let currentTime = JulianDate.fromIso8601("2026-07-16T00:00:00Z");
  let trackedEntity: { id: string } | undefined;
  const positionWC = new Cartesian3(1, 0, 0);

  const camera = {
    positionWC,
    zoomIn: vi.fn(),
    zoomOut: vi.fn(),
    moveLeft: vi.fn(),
    moveRight: vi.fn(),
    moveUp: vi.fn(),
    moveDown: vi.fn(),
    lookLeft: vi.fn(),
    lookRight: vi.fn(),
    lookUp: vi.fn(),
    lookDown: vi.fn(),
    twistLeft: vi.fn(),
    twistRight: vi.fn(),
    lookAtTransform: vi.fn(
      (transform: Matrix4, offset?: HeadingPitchRange | Cartesian3) => {
        // clearLookAt 仅传 IDENTITY，无 offset。
        if (offset == null) {
          return;
        }
        const hpr =
          offset instanceof HeadingPitchRange
            ? offset
            : headingPitchRangeFromLocalOffset(offset);
        const local = localOffsetFromHeadingPitchRange(
          hpr.heading,
          hpr.pitch,
          hpr.range,
        );
        Matrix4.multiplyByPoint(transform, local, positionWC);
      },
    ),
  };

  const flyTo = vi.fn(async () => {
    if (options?.flyToResult !== undefined) {
      return options.flyToResult;
    }
    return true;
  });

  const viewer = {
    flyTo,
    get trackedEntity() {
      return trackedEntity;
    },
    set trackedEntity(value: { id: string } | undefined) {
      trackedEntity = value;
    },
    camera,
    clock: {
      get currentTime() {
        return currentTime;
      },
      set currentTime(value: JulianDate) {
        currentTime = value;
      },
      onTick: {
        addEventListener: vi.fn((listener: TickListener) => {
          tickListeners.add(listener);
          return () => {
            removeTick();
            tickListeners.delete(listener);
          };
        }),
      },
    },
  };

  return {
    viewer,
    camera,
    flyTo,
    removeTick,
    tickListeners,
    setCurrentTime: (iso: string) => {
      currentTime = JulianDate.fromIso8601(iso);
    },
    emitTick: () => {
      for (const listener of [...tickListeners]) {
        listener({ currentTime });
      }
    },
    getPositionWC: () => Cartesian3.clone(positionWC),
  };
}

it("HPR 与本地 offset 可往返转换", () => {
  const heading = CesiumMath.toRadians(35);
  const pitch = CesiumMath.toRadians(-25);
  const range = 1234;
  const local = localOffsetFromHeadingPitchRange(heading, pitch, range);
  const recovered = headingPitchRangeFromLocalOffset(local);
  expect(recovered.range).toBeCloseTo(range, 6);
  expect(recovered.heading).toBeCloseTo(heading, 6);
  expect(recovered.pitch).toBeCloseTo(pitch, 6);
});

it("生产 adapter：flyTo 返回 false 时抛错", async () => {
  const fake = createViewerLikeFake({ flyToResult: false });
  const entity = { id: "iss" };
  const adapter = createCesiumCameraViewerAdapter(
    fake.viewer as never,
    () => entity as never,
  );

  await expect(adapter.flyTo(entity as never)).rejects.toThrow(
    /flyTo|未能完成/i,
  );
});

it("生产 adapter：映射 camera 相对运动 API", () => {
  const fake = createViewerLikeFake();
  const adapter = createCesiumCameraViewerAdapter(
    fake.viewer as never,
    () => undefined,
  );

  adapter.zoomIn(11);
  adapter.zoomOut(12);
  adapter.moveLeft(13);
  adapter.moveRight(14);
  adapter.moveUp(15);
  adapter.moveDown(16);
  adapter.lookLeft(0.1);
  adapter.lookRight(0.2);
  adapter.lookUp(0.3);
  adapter.lookDown(0.4);
  adapter.twistLeft(0.5);
  adapter.twistRight(0.6);

  expect(fake.camera.zoomIn).toHaveBeenCalledWith(11);
  expect(fake.camera.zoomOut).toHaveBeenCalledWith(12);
  expect(fake.camera.moveLeft).toHaveBeenCalledWith(13);
  expect(fake.camera.moveRight).toHaveBeenCalledWith(14);
  expect(fake.camera.moveUp).toHaveBeenCalledWith(15);
  expect(fake.camera.moveDown).toHaveBeenCalledWith(16);
  expect(fake.camera.lookLeft).toHaveBeenCalledWith(0.1);
  expect(fake.camera.lookRight).toHaveBeenCalledWith(0.2);
  expect(fake.camera.lookUp).toHaveBeenCalledWith(0.3);
  expect(fake.camera.lookDown).toHaveBeenCalledWith(0.4);
  expect(fake.camera.twistLeft).toHaveBeenCalledWith(0.5);
  expect(fake.camera.twistRight).toHaveBeenCalledWith(0.6);
});

it("生产 adapter：JulianDate secondsDifference 与 tick add/remove", () => {
  const fake = createViewerLikeFake();
  const adapter = createCesiumCameraViewerAdapter(
    fake.viewer as never,
    () => undefined,
  );

  const earlier = adapter.cloneTime(adapter.getCurrentTime());
  fake.setCurrentTime("2026-07-16T00:00:05Z");
  const later = adapter.getCurrentTime();
  expect(adapter.secondsDifference(later, earlier)).toBeCloseTo(5, 6);

  const listener = vi.fn();
  const unsubscribe = adapter.addTickListener(listener);
  expect(fake.tickListeners.size).toBe(1);
  fake.emitTick();
  expect(listener).toHaveBeenCalledOnce();
  unsubscribe();
  expect(fake.removeTick).toHaveBeenCalledOnce();
  expect(fake.tickListeners.size).toBe(0);
});

it("生产 adapter：读取目标参考系下相对 HeadingPitchRange", () => {
  const fake = createViewerLikeFake();
  const target = new Cartesian3(6378137, 0, 0);
  const entity = {
    id: "iss",
    position: {
      getValue: () => target,
    },
  };
  const adapter = createCesiumCameraViewerAdapter(
    fake.viewer as never,
    () => entity as never,
  );

  const heading = CesiumMath.toRadians(40);
  const pitch = CesiumMath.toRadians(-30);
  const range = 2500;
  const transform = Transforms.eastNorthUpToFixedFrame(target);
  adapter.lookAtTransform(transform, { heading, pitch, range });

  const read = adapter.getLookAtHeadingPitchRange({
    x: target.x,
    y: target.y,
    z: target.z,
  });
  expect(read.range).toBeCloseTo(range, 4);
  expect(read.heading).toBeCloseTo(heading, 4);
  expect(read.pitch).toBeCloseTo(pitch, 4);
});

it("生产 adapter：clearLookAt 使用 IDENTITY 解除约束", () => {
  const fake = createViewerLikeFake();
  const adapter = createCesiumCameraViewerAdapter(
    fake.viewer as never,
    () => undefined,
  );

  adapter.clearLookAt();

  expect(fake.camera.lookAtTransform).toHaveBeenCalledWith(Matrix4.IDENTITY);
});

function createOrbitInputHandlerHarness() {
  type HandlerAction = (...args: unknown[]) => void;
  const actions = new Map<number, HandlerAction>();
  const destroy = vi.fn();
  const canvas = {} as HTMLCanvasElement;
  const createHandler = vi.fn(() => ({
    setInputAction: (action: HandlerAction, type: number) => {
      actions.set(type, action);
    },
    destroy,
  }));
  const viewer = { scene: { canvas } };
  const adapter = createCesiumOrbitUserInputAdapter(
    viewer as never,
    createHandler,
  );
  return { actions, destroy, canvas, createHandler, adapter };
}

it("生产 orbit 输入 adapter：拖拽/滚轮触发；未注册 LEFT_CLICK", () => {
  const { actions, destroy, canvas, createHandler, adapter } =
    createOrbitInputHandlerHarness();
  const onGesture = vi.fn();
  const unsubscribe = adapter.subscribe(onGesture);

  expect(createHandler).toHaveBeenCalledWith(canvas);
  expect(actions.has(ScreenSpaceEventType.LEFT_CLICK)).toBe(false);
  expect(actions.has(ScreenSpaceEventType.MOUSE_MOVE)).toBe(true);
  expect(actions.has(ScreenSpaceEventType.WHEEL)).toBe(true);

  actions.get(ScreenSpaceEventType.LEFT_DOWN)?.();
  actions.get(ScreenSpaceEventType.MOUSE_MOVE)?.();
  expect(onGesture).toHaveBeenCalledWith("leftDrag");

  onGesture.mockClear();
  actions.get(ScreenSpaceEventType.LEFT_UP)?.();
  actions.get(ScreenSpaceEventType.MIDDLE_DOWN)?.();
  actions.get(ScreenSpaceEventType.MOUSE_MOVE)?.();
  expect(onGesture).toHaveBeenCalledWith("middleDrag");

  onGesture.mockClear();
  actions.get(ScreenSpaceEventType.MIDDLE_UP)?.();
  actions.get(ScreenSpaceEventType.RIGHT_DOWN)?.();
  actions.get(ScreenSpaceEventType.MOUSE_MOVE)?.();
  expect(onGesture).toHaveBeenCalledWith("rightDrag");

  onGesture.mockClear();
  actions.get(ScreenSpaceEventType.WHEEL)?.();
  expect(onGesture).toHaveBeenCalledWith("wheel");

  unsubscribe();
  expect(destroy).toHaveBeenCalledOnce();
});

it("生产 orbit 输入 adapter：无按键按下时 MOUSE_MOVE（悬停）不调用 onGesture", () => {
  const { actions, adapter } = createOrbitInputHandlerHarness();
  const onGesture = vi.fn();
  adapter.subscribe(onGesture);

  expect(actions.has(ScreenSpaceEventType.LEFT_CLICK)).toBe(false);

  // 悬停：未 LEFT/MIDDLE/RIGHT_DOWN 仅移动
  actions.get(ScreenSpaceEventType.MOUSE_MOVE)?.();
  expect(onGesture).not.toHaveBeenCalled();

  // 松开后再次移动仍不触发
  actions.get(ScreenSpaceEventType.LEFT_DOWN)?.();
  actions.get(ScreenSpaceEventType.LEFT_UP)?.();
  onGesture.mockClear();
  actions.get(ScreenSpaceEventType.MOUSE_MOVE)?.();
  expect(onGesture).not.toHaveBeenCalled();
});

it("生产 adapter + 控制器：orbitStep 相对当前非零 heading 增量", async () => {
  const fake = createViewerLikeFake();
  const target = new Cartesian3(6378137, 0, 0);
  const entity = {
    id: "iss",
    position: {
      getValue: () => target,
    },
  };
  const adapter = createCesiumCameraViewerAdapter(
    fake.viewer as never,
    (id) => (id === "iss" ? (entity as never) : undefined),
  );

  const { CesiumCameraController } = await import("./CesiumCameraController");
  const controller = new CesiumCameraController(adapter);

  const initialHeading = CesiumMath.toRadians(25);
  const pitch = CesiumMath.toRadians(-18);
  const range = 900;
  adapter.lookAtTransform(Transforms.eastNorthUpToFixedFrame(target), {
    heading: initialHeading,
    pitch,
    range,
  });

  await controller.apply({
    op: "camera",
    action: "orbitStep",
    targetId: "iss",
    amount: 15,
  });

  const after = adapter.getLookAtHeadingPitchRange({
    x: target.x,
    y: target.y,
    z: target.z,
  });
  expect(after.heading).toBeCloseTo(initialHeading + CesiumMath.toRadians(15), 4);
  expect(after.pitch).toBeCloseTo(pitch, 4);
  expect(after.range).toBeCloseTo(range, 4);
});
