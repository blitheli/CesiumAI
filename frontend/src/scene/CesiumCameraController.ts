import {
  Cartesian3,
  HeadingPitchRange,
  JulianDate,
  Math as CesiumMath,
  Matrix3,
  Matrix4,
  Quaternion,
  ScreenSpaceEventHandler,
  ScreenSpaceEventType,
  Transforms,
  type Entity,
  type Viewer,
} from "cesium";
import type { CameraSceneOp } from "../contracts/chat";

/** 会解除持续环绕的用户手势（点击选中/悬停不在此列）。 */
export type OrbitCancelGesture =
  | "leftDrag"
  | "middleDrag"
  | "rightDrag"
  | "wheel";

/** 环绕期间用户输入适配面：单测用 fake，生产用 ScreenSpaceEventHandler。 */
export type OrbitUserInputAdapter = {
  /** 注册解除环绕手势；返回取消订阅/销毁函数。 */
  subscribe(onGesture: (gesture: OrbitCancelGesture) => void): () => void;
};

const noopOrbitUserInput: OrbitUserInputAdapter = {
  subscribe: () => () => undefined,
};

/** 与 Cesium Camera.lookAtTransform 一致的 HPR→本地 offset（用于测试与生产读取）。 */
export function localOffsetFromHeadingPitchRange(
  heading: number,
  pitch: number,
  range: number,
): Cartesian3 {
  const clampedPitch = CesiumMath.clamp(
    pitch,
    -CesiumMath.PI_OVER_TWO,
    CesiumMath.PI_OVER_TWO,
  );
  const adjustedHeading =
    CesiumMath.zeroToTwoPi(heading) - CesiumMath.PI_OVER_TWO;

  const pitchQuat = Quaternion.fromAxisAngle(
    Cartesian3.UNIT_Y,
    -clampedPitch,
  );
  const headingQuat = Quaternion.fromAxisAngle(
    Cartesian3.UNIT_Z,
    -adjustedHeading,
  );
  const rotQuat = Quaternion.multiply(headingQuat, pitchQuat, headingQuat);
  const rotMatrix = Matrix3.fromQuaternion(rotQuat);
  const offset = Matrix3.multiplyByVector(
    rotMatrix,
    Cartesian3.clone(Cartesian3.UNIT_X),
    new Cartesian3(),
  );
  Cartesian3.negate(offset, offset);
  return Cartesian3.multiplyByScalar(offset, range, offset);
}

/** 从目标 ENU 本地 offset 反推 HeadingPitchRange。 */
export function headingPitchRangeFromLocalOffset(offset: Cartesian3): {
  heading: number;
  pitch: number;
  range: number;
} {
  const range = Cartesian3.magnitude(offset);
  if (!(range > 0)) {
    throw new Error("无法从零距离 offset 推导 HeadingPitchRange。");
  }
  // 与 localOffsetFromHeadingPitchRange / Cesium lookAtTransform 约定对齐：
  // 相机在目标上方时 local.z>0，对应负 pitch（俯视）。
  const heading = CesiumMath.zeroToTwoPi(
    Math.atan2(offset.x, offset.y) + Math.PI,
  );
  const pitch = -Math.asin(offset.z / range);
  return { heading, pitch, range };
}

/** 相机实体最小适配面，便于单测注入 fake。 */
export type CameraEntityAdapter = {
  id: string;
  position?: {
    getValue(time: unknown): { x: number; y: number; z: number } | undefined;
  };
};

/** 相机 Viewer 适配面：单测用 fake，生产用 Cesium API。 */
export type CameraViewerAdapter = {
  flyTo(
    entity: CameraEntityAdapter,
    options?: {
      offset?: { heading: number; pitch: number; range: number };
    },
  ): Promise<void>;
  getTrackedEntity(): CameraEntityAdapter | undefined;
  setTrackedEntity(entity: CameraEntityAdapter | undefined): void;
  zoomIn(amount: number): void;
  zoomOut(amount: number): void;
  moveLeft(amount: number): void;
  moveRight(amount: number): void;
  moveUp(amount: number): void;
  moveDown(amount: number): void;
  lookLeft(amount: number): void;
  lookRight(amount: number): void;
  lookUp(amount: number): void;
  lookDown(amount: number): void;
  twistLeft(amount: number): void;
  twistRight(amount: number): void;
  getCurrentTime(): unknown;
  secondsDifference(later: unknown, earlier: unknown): number;
  cloneTime(time: unknown): unknown;
  addTickListener(
    listener: (clock: { currentTime: unknown }) => void,
  ): () => void;
  lookAtTransform(
    transform: unknown,
    offset: { heading: number; pitch: number; range: number },
  ): void;
  /** 清除 lookAt 约束，恢复自由相机控制。 */
  clearLookAt(): void;
  eastNorthUpToFixedFrame(position: {
    x: number;
    y: number;
    z: number;
  }): unknown;
  /** 读取相机在目标 ENU 参考系下的当前 HeadingPitchRange（弧度/米）。 */
  getLookAtHeadingPitchRange(targetPosition: {
    x: number;
    y: number;
    z: number;
  }): { heading: number; pitch: number; range: number };
  /** 只读：当前相机世界坐标，供诊断观测，不改变控制器状态。 */
  getCameraPositionWC(): { x: number; y: number; z: number } | undefined;
  /** 只读：当前相机 heading（度）。 */
  getCameraHeadingDegrees(): number | undefined;
  getEntityById(id: string): CameraEntityAdapter | undefined;
};

/** 只读相机诊断；不得用于绕过真实控制器执行动作。 */
export type CameraDiagnostics = {
  trackedEntityId: string | null;
  orbitActive: boolean;
  orbitTargetId: string | null;
  /** 持续环绕时的当前航向（度），便于观测环绕推进。 */
  orbitHeadingDegrees: number | null;
  /** 生产相机当前 heading（度，Cesium 约定）；用于相对转向方向断言。 */
  headingDegrees: number | null;
  positionWC: [number, number, number] | null;
};

/** upsert/style 替换实体时的事务化重绑句柄。 */
export type EntityReplacementTransaction = {
  commit(): void;
  rollback(): void;
};

export interface CameraControllerPort {
  apply(operation: CameraSceneOp): Promise<void>;
  onSceneCleared(): void;
  onEntitiesDeleted(ids: string[]): void;
  /**
   * 实体替换事务：若正在 track 被替换的 ID，则在 commit 后重绑到新 Entity。
   */
  beginEntityReplacement(ids: readonly string[]): EntityReplacementTransaction;
  /** load 重建文档前保存当前 tracked id；无 track 时返回 null。 */
  snapshotTrackedTargetId(): string | null;
  /** load 后按快照 id 重绑当前新 Entity；null/找不到时 no-op。 */
  rebindAfterReload(trackedTargetId: string | null): void;
  destroy(): void;
  /** 只读观测当前跟随/环绕/相机位置。 */
  getDiagnostics(): CameraDiagnostics;
}

const DEFAULT_ORBIT_HEADING_DEGREES = 0;
const DEFAULT_ORBIT_PITCH_DEGREES = -45;
const DEFAULT_ORBIT_RANGE_METERS = 1000;
const DEFAULT_PAN_AMOUNT_METERS = 100;

type OrbitState = {
  targetId: string;
  headingRadians: number;
  pitchRadians: number;
  rangeMeters: number;
  angularSpeedRadiansPerSecond: number;
  lastTickTime: unknown;
  unsubscribe: () => void;
  unsubscribeInput: () => void;
};

function degreesToRadians(degrees: number): number {
  return CesiumMath.toRadians(degrees);
}

function requireTargetId(targetId: string | null | undefined): string {
  if (typeof targetId !== "string" || targetId.trim().length === 0) {
    throw new Error("相机操作缺少有效目标实体 ID。");
  }
  return targetId;
}

/**
 * 基于 Viewer 适配面的 Cesium 相机控制器。
 * 负责 focus/track/相对微调与环绕；跟随与环绕互斥。
 */
export class CesiumCameraController implements CameraControllerPort {
  private readonly adapter: CameraViewerAdapter;
  private readonly orbitUserInput: OrbitUserInputAdapter;
  private orbit: OrbitState | undefined;
  private destroyed = false;

  constructor(
    adapter: CameraViewerAdapter,
    orbitUserInput: OrbitUserInputAdapter = noopOrbitUserInput,
  ) {
    this.adapter = adapter;
    this.orbitUserInput = orbitUserInput;
  }

  async apply(operation: CameraSceneOp): Promise<void> {
    this.ensureNotDestroyed();

    switch (operation.action) {
      case "focus":
        await this.focus(operation);
        return;
      case "track":
        this.track(operation);
        return;
      case "untrack":
        this.untrack();
        return;
      case "zoom":
        this.zoom(operation);
        return;
      case "pan":
        this.pan(operation);
        return;
      case "rotate":
        this.rotate(operation);
        return;
      case "orbitStep":
        this.orbitStep(operation);
        return;
      case "orbitStart":
        this.orbitStart(operation);
        return;
      case "orbitStop":
        this.stopOrbit();
        return;
      default: {
        const _exhaustive: never = operation.action;
        throw new Error(`未知相机动作：${String(_exhaustive)}`);
      }
    }
  }

  onSceneCleared(): void {
    if (this.destroyed) {
      return;
    }
    this.stopOrbit();
    this.adapter.setTrackedEntity(undefined);
  }

  onEntitiesDeleted(ids: string[]): void {
    if (this.destroyed) {
      return;
    }

    const deleted = new Set(ids);
    const tracked = this.adapter.getTrackedEntity();
    if (tracked && deleted.has(tracked.id)) {
      this.adapter.setTrackedEntity(undefined);
    }
    if (this.orbit && deleted.has(this.orbit.targetId)) {
      this.stopOrbit();
    }
  }

  destroy(): void {
    if (this.destroyed) {
      return;
    }
    this.stopOrbit();
    this.adapter.setTrackedEntity(undefined);
    this.destroyed = true;
  }

  getDiagnostics(): CameraDiagnostics {
    const tracked = this.adapter.getTrackedEntity();
    const position = this.adapter.getCameraPositionWC();
    const headingDegrees = this.adapter.getCameraHeadingDegrees();
    return {
      trackedEntityId: tracked?.id ?? null,
      orbitActive: this.orbit !== undefined,
      orbitTargetId: this.orbit?.targetId ?? null,
      orbitHeadingDegrees:
        this.orbit !== undefined
          ? CesiumMath.toDegrees(this.orbit.headingRadians)
          : null,
      headingDegrees:
        typeof headingDegrees === "number" && Number.isFinite(headingDegrees)
          ? headingDegrees
          : null,
      positionWC: position
        ? [position.x, position.y, position.z]
        : null,
    };
  }

  beginEntityReplacement(ids: readonly string[]): EntityReplacementTransaction {
    this.ensureNotDestroyed();
    const idSet = new Set(ids);
    const tracked = this.adapter.getTrackedEntity();
    const trackedId =
      tracked && idSet.has(tracked.id) ? tracked.id : null;

    const rebind = () => {
      if (!trackedId || this.destroyed) {
        return;
      }
      const entity = this.adapter.getEntityById(trackedId);
      if (entity) {
        this.adapter.setTrackedEntity(entity);
      }
    };

    return {
      commit: rebind,
      // rollback 由 SceneManager 在 load 后调用 rebindAfterReload（含跟踪非更新目标）。
      rollback: rebind,
    };
  }

  snapshotTrackedTargetId(): string | null {
    if (this.destroyed) {
      return null;
    }
    return this.adapter.getTrackedEntity()?.id ?? null;
  }

  rebindAfterReload(trackedTargetId: string | null): void {
    if (this.destroyed || trackedTargetId == null || trackedTargetId.trim() === "") {
      return;
    }
    const entity = this.adapter.getEntityById(trackedTargetId);
    if (entity) {
      this.adapter.setTrackedEntity(entity);
    }
  }

  private async focus(operation: CameraSceneOp): Promise<void> {
    // 先校验目标，失败不得改动既有 track/orbit。
    const targetId = requireTargetId(operation.targetId);
    const entity = this.requireEntity(targetId);

    const previousTracked = this.adapter.getTrackedEntity();
    const previousOrbitSnapshot = this.snapshotOrbitForRestore();

    // focus 必须退出 track/orbit，避免与 flyTo 竞争。
    this.stopOrbit();
    this.adapter.setTrackedEntity(undefined);

    const hasOffset =
      operation.distanceMeters != null ||
      operation.headingDegrees != null ||
      operation.pitchDegrees != null;

    try {
      if (!hasOffset) {
        await this.adapter.flyTo(entity);
      } else {
        await this.adapter.flyTo(entity, {
          offset: {
            heading: degreesToRadians(operation.headingDegrees ?? 0),
            pitch: degreesToRadians(
              operation.pitchDegrees ?? DEFAULT_ORBIT_PITCH_DEGREES,
            ),
            range: operation.distanceMeters ?? DEFAULT_ORBIT_RANGE_METERS,
          },
        });
      }
    } catch (error) {
      this.restoreTrackOrOrbit(previousTracked, previousOrbitSnapshot);
      throw error;
    }
    // 成功后旧模式保持清除。
  }

  private snapshotOrbitForRestore(): OrbitState | undefined {
    if (!this.orbit) {
      return undefined;
    }
    return {
      targetId: this.orbit.targetId,
      headingRadians: this.orbit.headingRadians,
      pitchRadians: this.orbit.pitchRadians,
      rangeMeters: this.orbit.rangeMeters,
      angularSpeedRadiansPerSecond: this.orbit.angularSpeedRadiansPerSecond,
      lastTickTime: this.adapter.cloneTime(this.orbit.lastTickTime),
      unsubscribe: () => undefined,
      unsubscribeInput: () => undefined,
    };
  }

  private restoreTrackOrOrbit(
    previousTracked: CameraEntityAdapter | undefined,
    previousOrbit: OrbitState | undefined,
  ): void {
    if (previousOrbit) {
      this.restoreOrbit(previousOrbit);
      return;
    }
    if (previousTracked) {
      this.adapter.setTrackedEntity(previousTracked);
    }
  }

  private restoreOrbit(snapshot: OrbitState): void {
    this.stopOrbit();
    this.adapter.setTrackedEntity(undefined);

    try {
      this.lookAtEntity(snapshot.targetId, {
        headingRadians: snapshot.headingRadians,
        pitchRadians: snapshot.pitchRadians,
        rangeMeters: snapshot.rangeMeters,
      });
    } catch {
      return;
    }

    const state: OrbitState = {
      ...snapshot,
      lastTickTime: this.adapter.cloneTime(this.adapter.getCurrentTime()),
      unsubscribe: () => undefined,
      unsubscribeInput: () => undefined,
    };

    const onTick = (clock: { currentTime: unknown }) => {
      if (!this.orbit || this.orbit !== state) {
        return;
      }

      const dt = this.adapter.secondsDifference(
        clock.currentTime,
        state.lastTickTime,
      );
      state.lastTickTime = this.adapter.cloneTime(clock.currentTime);
      if (Number.isFinite(dt) && dt > 0) {
        state.headingRadians += state.angularSpeedRadiansPerSecond * dt;
      }

      try {
        this.lookAtEntity(state.targetId, {
          headingRadians: state.headingRadians,
          pitchRadians: state.pitchRadians,
          rangeMeters: state.rangeMeters,
        });
      } catch {
        this.stopOrbit();
      }
    };

    state.unsubscribe = this.adapter.addTickListener(onTick);
    state.unsubscribeInput = this.orbitUserInput.subscribe(() => {
      if (!this.orbit || this.orbit !== state) {
        return;
      }
      this.stopOrbit();
    });
    this.orbit = state;
  }

  private track(operation: CameraSceneOp): void {
    const targetId = requireTargetId(operation.targetId);
    // 先校验目标与当前位置，失败则保留现有 track/orbit。
    this.requireEntityWithPosition(targetId);
    this.stopOrbit();
    this.adapter.setTrackedEntity(this.requireEntity(targetId));
  }

  private untrack(): void {
    this.adapter.setTrackedEntity(undefined);
  }

  private zoom(operation: CameraSceneOp): void {
    const amount = operation.amount;
    if (amount == null || amount === 0 || !Number.isFinite(amount)) {
      throw new Error("zoom 需要非零有限 amount（米）；正数拉近，负数拉远。");
    }
    if (amount > 0) {
      this.adapter.zoomIn(amount);
    } else {
      this.adapter.zoomOut(Math.abs(amount));
    }
  }

  private pan(operation: CameraSceneOp): void {
    const direction = operation.direction;
    if (
      direction !== "left" &&
      direction !== "right" &&
      direction !== "up" &&
      direction !== "down"
    ) {
      throw new Error("pan 需要 direction：left|right|up|down。");
    }

    const amount =
      operation.amount == null ? DEFAULT_PAN_AMOUNT_METERS : operation.amount;
    if (!Number.isFinite(amount) || amount <= 0) {
      throw new Error("pan 的 amount 必须是正有限距离（米）。");
    }

    switch (direction) {
      case "left":
        this.adapter.moveLeft(amount);
        break;
      case "right":
        this.adapter.moveRight(amount);
        break;
      case "up":
        this.adapter.moveUp(amount);
        break;
      case "down":
        this.adapter.moveDown(amount);
        break;
    }
  }

  private rotate(operation: CameraSceneOp): void {
    const heading = operation.headingDegrees;
    const pitch = operation.pitchDegrees;
    const roll = operation.rollDegrees;

    if (heading != null && Number.isFinite(heading) && heading !== 0) {
      const radians = degreesToRadians(Math.abs(heading));
      if (heading > 0) {
        this.adapter.lookRight(radians);
      } else {
        this.adapter.lookLeft(radians);
      }
    }

    if (pitch != null && Number.isFinite(pitch) && pitch !== 0) {
      const radians = degreesToRadians(Math.abs(pitch));
      if (pitch > 0) {
        this.adapter.lookUp(radians);
      } else {
        this.adapter.lookDown(radians);
      }
    }

    if (roll != null && Number.isFinite(roll) && roll !== 0) {
      const radians = degreesToRadians(Math.abs(roll));
      if (roll > 0) {
        this.adapter.twistRight(radians);
      } else {
        this.adapter.twistLeft(radians);
      }
    }
  }

  private orbitStep(operation: CameraSceneOp): void {
    const targetId = requireTargetId(operation.targetId);
    const amount = operation.amount;
    if (amount == null || amount === 0 || !Number.isFinite(amount)) {
      throw new Error("orbitStep 需要非零有限 amount（度）。");
    }

    const position = this.requireEntityWithPosition(targetId);
    const current =
      this.orbit?.targetId === targetId
        ? {
            heading: this.orbit.headingRadians,
            pitch: this.orbit.pitchRadians,
            range: this.orbit.rangeMeters,
          }
        : this.adapter.getLookAtHeadingPitchRange(position);

    // targetHeading = (headingDegrees ?? 当前视角 heading 转度) + amount
    const baseHeadingDegrees =
      operation.headingDegrees ?? CesiumMath.toDegrees(current.heading);
    const headingRadians = degreesToRadians(baseHeadingDegrees + amount);
    // pitch/distance：传入则覆盖，否则沿用当前相对目标值
    const pitchRadians =
      operation.pitchDegrees != null
        ? degreesToRadians(operation.pitchDegrees)
        : current.pitch;
    const rangeMeters =
      operation.distanceMeters != null
        ? operation.distanceMeters
        : current.range;

    // 校验通过后再停止现有跟随/环绕。
    this.adapter.setTrackedEntity(undefined);
    this.stopOrbit();

    this.lookAtEntity(targetId, {
      headingRadians,
      pitchRadians,
      rangeMeters,
    });
  }

  private orbitStart(operation: CameraSceneOp): void {
    const targetId = requireTargetId(operation.targetId);
    const speedDegrees = operation.angularSpeedDegreesPerSecond;
    if (
      speedDegrees == null ||
      !Number.isFinite(speedDegrees) ||
      speedDegrees <= 0
    ) {
      throw new Error("orbitStart 需要大于 0 的 angularSpeedDegreesPerSecond。");
    }

    // 先校验目标位置，失败不得清掉现有控制。
    this.requireEntityWithPosition(targetId);

    const headingRadians = degreesToRadians(
      operation.headingDegrees ?? DEFAULT_ORBIT_HEADING_DEGREES,
    );
    const pitchRadians = degreesToRadians(
      operation.pitchDegrees ?? DEFAULT_ORBIT_PITCH_DEGREES,
    );
    const rangeMeters =
      operation.distanceMeters ?? DEFAULT_ORBIT_RANGE_METERS;

    this.adapter.setTrackedEntity(undefined);
    this.stopOrbit();

    this.lookAtEntity(targetId, {
      headingRadians,
      pitchRadians,
      rangeMeters,
    });

    const lastTickTime = this.adapter.cloneTime(this.adapter.getCurrentTime());
    const state: OrbitState = {
      targetId,
      headingRadians,
      pitchRadians,
      rangeMeters,
      angularSpeedRadiansPerSecond: degreesToRadians(speedDegrees),
      lastTickTime,
      unsubscribe: () => undefined,
      unsubscribeInput: () => undefined,
    };

    const onTick = (clock: { currentTime: unknown }) => {
      if (!this.orbit || this.orbit !== state) {
        return;
      }

      const dt = this.adapter.secondsDifference(
        clock.currentTime,
        state.lastTickTime,
      );
      state.lastTickTime = this.adapter.cloneTime(clock.currentTime);
      if (Number.isFinite(dt) && dt > 0) {
        state.headingRadians += state.angularSpeedRadiansPerSecond * dt;
      }

      try {
        this.lookAtEntity(state.targetId, {
          headingRadians: state.headingRadians,
          pitchRadians: state.pitchRadians,
          rangeMeters: state.rangeMeters,
        });
      } catch {
        this.stopOrbit();
      }
    };

    state.unsubscribe = this.adapter.addTickListener(onTick);
    state.unsubscribeInput = this.orbitUserInput.subscribe(() => {
      if (!this.orbit || this.orbit !== state) {
        return;
      }
      this.stopOrbit();
    });
    this.orbit = state;
  }

  private stopOrbit(): void {
    if (!this.orbit) {
      return;
    }
    const { unsubscribe, unsubscribeInput } = this.orbit;
    this.orbit = undefined;
    unsubscribe();
    unsubscribeInput();
    this.adapter.clearLookAt();
  }

  private lookAtEntity(
    targetId: string,
    offset: {
      headingRadians: number;
      pitchRadians: number;
      rangeMeters: number;
    },
  ): void {
    const position = this.requireEntityWithPosition(targetId);
    const transform = this.adapter.eastNorthUpToFixedFrame(position);
    this.adapter.lookAtTransform(transform, {
      heading: offset.headingRadians,
      pitch: offset.pitchRadians,
      range: offset.rangeMeters,
    });
  }

  private requireEntity(targetId: string): CameraEntityAdapter {
    const entity = this.adapter.getEntityById(targetId);
    if (!entity) {
      throw new Error(`相机目标实体不存在：'${targetId}'。`);
    }
    return entity;
  }

  private requireEntityWithPosition(targetId: string): {
    x: number;
    y: number;
    z: number;
  } {
    const entity = this.requireEntity(targetId);
    const position = entity.position?.getValue(this.adapter.getCurrentTime());
    if (!position) {
      throw new Error(
        `相机目标 '${targetId}' 在当前时刻没有有效位置（position）。`,
      );
    }
    return position;
  }

  private ensureNotDestroyed(): void {
    if (this.destroyed) {
      throw new Error("相机控制器已销毁，无法继续执行操作。");
    }
  }
}

/** 用真实 Cesium Viewer 构造生产适配器。 */
export function createCesiumCameraViewerAdapter(
  viewer: Viewer,
  getEntityById: (id: string) => Entity | undefined,
): CameraViewerAdapter {
  return {
    flyTo: async (entity, options) => {
      const cesiumEntity = entity as Entity;
      const completed = options?.offset
        ? await viewer.flyTo(cesiumEntity, {
            offset: new HeadingPitchRange(
              options.offset.heading,
              options.offset.pitch,
              options.offset.range,
            ),
          })
        : await viewer.flyTo(cesiumEntity);

      if (completed === false) {
        throw new Error("相机 flyTo 未能完成。");
      }
    },
    getTrackedEntity: () => viewer.trackedEntity,
    setTrackedEntity: (entity) => {
      viewer.trackedEntity = (entity as Entity | undefined) ?? undefined;
    },
    zoomIn: (amount) => {
      viewer.camera.zoomIn(amount);
    },
    zoomOut: (amount) => {
      viewer.camera.zoomOut(amount);
    },
    moveLeft: (amount) => {
      viewer.camera.moveLeft(amount);
    },
    moveRight: (amount) => {
      viewer.camera.moveRight(amount);
    },
    moveUp: (amount) => {
      viewer.camera.moveUp(amount);
    },
    moveDown: (amount) => {
      viewer.camera.moveDown(amount);
    },
    lookLeft: (amount) => {
      viewer.camera.lookLeft(amount);
    },
    lookRight: (amount) => {
      viewer.camera.lookRight(amount);
    },
    lookUp: (amount) => {
      viewer.camera.lookUp(amount);
    },
    lookDown: (amount) => {
      viewer.camera.lookDown(amount);
    },
    twistLeft: (amount) => {
      viewer.camera.twistLeft(amount);
    },
    twistRight: (amount) => {
      viewer.camera.twistRight(amount);
    },
    getCurrentTime: () => viewer.clock.currentTime,
    secondsDifference: (later, earlier) =>
      JulianDate.secondsDifference(later as JulianDate, earlier as JulianDate),
    cloneTime: (time) => JulianDate.clone(time as JulianDate),
    addTickListener: (listener) => {
      const remove = viewer.clock.onTick.addEventListener((clock) => {
        listener({ currentTime: clock.currentTime });
      });
      return () => {
        remove();
      };
    },
    lookAtTransform: (transform, offset) => {
      viewer.camera.lookAtTransform(
        transform as Matrix4,
        new HeadingPitchRange(offset.heading, offset.pitch, offset.range),
      );
    },
    clearLookAt: () => {
      viewer.camera.lookAtTransform(Matrix4.IDENTITY);
    },
    eastNorthUpToFixedFrame: (position) =>
      Transforms.eastNorthUpToFixedFrame(
        position as Parameters<typeof Transforms.eastNorthUpToFixedFrame>[0],
      ),
    getLookAtHeadingPitchRange: (targetPosition) => {
      const transform = Transforms.eastNorthUpToFixedFrame(
        targetPosition as Parameters<
          typeof Transforms.eastNorthUpToFixedFrame
        >[0],
      );
      const inverse = Matrix4.inverseTransformation(transform, new Matrix4());
      const localOffset = Matrix4.multiplyByPoint(
        inverse,
        viewer.camera.positionWC,
        new Cartesian3(),
      );
      return headingPitchRangeFromLocalOffset(localOffset);
    },
    getCameraPositionWC: () => {
      const position = viewer.camera?.positionWC;
      if (!position) {
        return undefined;
      }
      return { x: position.x, y: position.y, z: position.z };
    },
    getCameraHeadingDegrees: () => {
      const heading = viewer.camera?.heading;
      if (typeof heading !== "number" || !Number.isFinite(heading)) {
        return undefined;
      }
      return CesiumMath.toDegrees(heading);
    },
    getEntityById: (id) => getEntityById(id),
  };
}

/** ScreenSpaceEventHandler 最小面，便于单测注入。 */
export type OrbitScreenSpaceEventHandler = {
  setInputAction(
    action: (...args: unknown[]) => void,
    type: number,
  ): void;
  destroy(): void;
};

/** 生产用：监听左键拖拽/中右键拖拽/滚轮，点击与悬停不触发。 */
export function createCesiumOrbitUserInputAdapter(
  viewer: Viewer,
  createHandler: (
    element: HTMLCanvasElement,
  ) => OrbitScreenSpaceEventHandler = (element) =>
    new ScreenSpaceEventHandler(element),
): OrbitUserInputAdapter {
  return {
    subscribe(onGesture) {
      const handler = createHandler(viewer.scene.canvas);
      let leftDown = false;
      let middleDown = false;
      let rightDown = false;

      handler.setInputAction(() => {
        leftDown = true;
      }, ScreenSpaceEventType.LEFT_DOWN);
      handler.setInputAction(() => {
        leftDown = false;
      }, ScreenSpaceEventType.LEFT_UP);

      handler.setInputAction(() => {
        middleDown = true;
      }, ScreenSpaceEventType.MIDDLE_DOWN);
      handler.setInputAction(() => {
        middleDown = false;
      }, ScreenSpaceEventType.MIDDLE_UP);

      handler.setInputAction(() => {
        rightDown = true;
      }, ScreenSpaceEventType.RIGHT_DOWN);
      handler.setInputAction(() => {
        rightDown = false;
      }, ScreenSpaceEventType.RIGHT_UP);

      handler.setInputAction(() => {
        if (leftDown) {
          onGesture("leftDrag");
          return;
        }
        if (middleDown) {
          onGesture("middleDrag");
          return;
        }
        if (rightDown) {
          onGesture("rightDrag");
        }
      }, ScreenSpaceEventType.MOUSE_MOVE);

      handler.setInputAction(() => {
        onGesture("wheel");
      }, ScreenSpaceEventType.WHEEL);

      return () => {
        handler.destroy();
      };
    },
  };
}

export function createCesiumCameraController(
  viewer: Viewer,
  getEntityById: (id: string) => Entity | undefined,
): CameraControllerPort {
  return new CesiumCameraController(
    createCesiumCameraViewerAdapter(viewer, getEntityById),
    createCesiumOrbitUserInputAdapter(viewer),
  );
}
