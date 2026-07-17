import {
  ClockRange,
  CzmlDataSource,
  JulianDate,
  type Viewer,
} from "cesium";
import type {
  CzmlPacket,
  SceneOp,
  SceneSummary,
} from "../contracts/chat";
import { createEmptyDocument } from "./emptyDocument";
import { reduceSceneDocument } from "./sceneDocument";
import {
  buildSceneSummary,
  pickRelevantPackets as selectRelevantPackets,
} from "./summary";
import { assertNever } from "../contracts/assertNever";
import {
  createCesiumCameraController,
  type CameraControllerPort,
  type CameraDiagnostics,
} from "./CesiumCameraController";

export interface CzmlDataSourcePort {
  load(packets: CzmlPacket[]): Promise<unknown>;
  process(packets: CzmlPacket[]): Promise<unknown>;
  removeById(id: string): boolean;
  syncViewerClock(clock: CzmlDocumentClock): void;
  snapshotViewerClock(): ViewerClockSnapshot;
  restoreViewerClock(snapshot: ViewerClockSnapshot): void;
  getSceneDiagnostics(): SceneDiagnostics;
}

export type EmptyDocumentFactory = () => CzmlPacket[];

export type CzmlDocumentClock = {
  interval: string;
  currentTime: string;
  multiplier?: number;
};

export type ViewerClockSnapshot = {
  startTime: JulianDate;
  stopTime: JulianDate;
  currentTime: JulianDate;
  clockRange: ClockRange;
  multiplier: number;
  shouldAnimate: boolean;
};

export type SceneEntityDiagnostics = {
  id: string;
  hasPosition: boolean;
  hasPositionAtCurrentTime: boolean;
  hasPoint: boolean;
  hasPath: boolean;
  hasCanonicalPosition?: boolean;
  /** canonical packet 中 Position 采样点数量（只读，用于样式后保留校验）。 */
  canonicalPositionSampleCount?: number;
  positionAtCurrentTime?: [number, number, number];
  pointPixelSize?: number;
  pointColorRgba?: [number, number, number, number];
  pathWidth?: number;
};

export type SceneDiagnostics = {
  clock?: {
    startTime: string;
    stopTime: string;
    currentTime: string;
  };
  camera?: CameraDiagnostics;
  entities: SceneEntityDiagnostics[];
};

function cloneDocument(document: CzmlPacket[]): CzmlPacket[] {
  return structuredClone(document);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function clockFromDocument(
  document: CzmlPacket[],
): CzmlDocumentClock | undefined {
  const clock = document.find((packet) => packet.id === "document")?.clock;
  if (
    !isRecord(clock) ||
    typeof clock.interval !== "string" ||
    typeof clock.currentTime !== "string"
  ) {
    return undefined;
  }

  return {
    interval: clock.interval,
    currentTime: clock.currentTime,
    ...(typeof clock.multiplier === "number"
      ? { multiplier: clock.multiplier }
      : {}),
  };
}

function applyAvailabilityClock(
  document: CzmlPacket[],
  packets: CzmlPacket[],
): CzmlPacket[] {
  const availability = packets
    .map((packet) => packet.availability)
    .filter((value): value is string => typeof value === "string")
    .at(-1);
  const separator = availability?.indexOf("/") ?? -1;
  if (!availability || separator <= 0 || separator >= availability.length - 1) {
    return document;
  }

  const currentTime = availability.slice(0, separator);
  return document.map((packet) => {
    if (packet.id !== "document") {
      return packet;
    }

    const existingClock = isRecord(packet.clock) ? packet.clock : {};
    return {
      ...packet,
      clock: {
        ...existingClock,
        interval: availability,
        currentTime,
      },
    };
  });
}

function lastPacketPerId(packets: CzmlPacket[]): CzmlPacket[] {
  const packetsById = new Map<string, CzmlPacket>();
  for (const packet of packets) {
    if (packet.id !== "document") {
      packetsById.set(packet.id, packet);
    }
  }
  return [...packetsById.values()];
}

class CesiumCzmlDataSourcePort implements CzmlDataSourcePort {
  private readonly viewer: Viewer;
  private readonly dataSource: CzmlDataSource;
  private attached = false;

  constructor(viewer: Viewer) {
    this.viewer = viewer;
    this.dataSource = new CzmlDataSource("scene");
  }

  getEntityById(id: string) {
    return this.dataSource.entities.getById(id);
  }

  async load(packets: CzmlPacket[]): Promise<unknown> {
    const result = await this.dataSource.load(packets);
    if (!this.attached) {
      await this.viewer.dataSources.add(this.dataSource);
      this.attached = true;
    }
    return result;
  }

  process(packets: CzmlPacket[]): Promise<unknown> {
    return this.dataSource.process(packets);
  }

  removeById(id: string): boolean {
    return this.dataSource.entities.removeById(id);
  }

  syncViewerClock(clock: CzmlDocumentClock): void {
    const [startIso, stopIso] = clock.interval.split("/");
    const startTime = JulianDate.fromIso8601(startIso!);
    const stopTime = JulianDate.fromIso8601(stopIso!);
    let currentTime = JulianDate.fromIso8601(clock.currentTime);
    if (
      JulianDate.lessThan(currentTime, startTime) ||
      JulianDate.greaterThan(currentTime, stopTime)
    ) {
      currentTime = JulianDate.clone(startTime);
    }
    const viewerClock = this.viewer.clock;
    viewerClock.startTime = JulianDate.clone(startTime);
    viewerClock.stopTime = JulianDate.clone(stopTime);
    viewerClock.currentTime = JulianDate.clone(currentTime);
    viewerClock.clockRange = ClockRange.LOOP_STOP;
    if (clock.multiplier !== undefined) {
      viewerClock.multiplier = clock.multiplier;
    }
    this.viewer.timeline.zoomTo(viewerClock.startTime, viewerClock.stopTime);
  }

  snapshotViewerClock(): ViewerClockSnapshot {
    const clock = this.viewer.clock;
    return {
      startTime: JulianDate.clone(clock.startTime),
      stopTime: JulianDate.clone(clock.stopTime),
      currentTime: JulianDate.clone(clock.currentTime),
      clockRange: clock.clockRange,
      multiplier: clock.multiplier,
      shouldAnimate: clock.shouldAnimate,
    };
  }

  restoreViewerClock(snapshot: ViewerClockSnapshot): void {
    const clock = this.viewer.clock;
    clock.startTime = JulianDate.clone(snapshot.startTime);
    clock.stopTime = JulianDate.clone(snapshot.stopTime);
    clock.currentTime = JulianDate.clone(snapshot.currentTime);
    clock.clockRange = snapshot.clockRange;
    clock.multiplier = snapshot.multiplier;
    clock.shouldAnimate = snapshot.shouldAnimate;
    this.viewer.timeline.zoomTo(clock.startTime, clock.stopTime);
  }

  getSceneDiagnostics(): SceneDiagnostics {
    const currentTime = this.viewer.clock.currentTime;
    const entities = this.dataSource.entities.values.map((entity) => {
      const position = entity.position?.getValue(currentTime);
      const pixelSize = entity.point?.pixelSize?.getValue(currentTime);
      const color = entity.point?.color?.getValue(currentTime);
      const pathWidth = entity.path?.width?.getValue(currentTime);
      const pointColorRgba =
        color &&
        typeof color.red === "number" &&
        typeof color.green === "number" &&
        typeof color.blue === "number" &&
        typeof color.alpha === "number"
          ? ([
              Math.round(color.red * 255),
              Math.round(color.green * 255),
              Math.round(color.blue * 255),
              Math.round(color.alpha * 255),
            ] as [number, number, number, number])
          : undefined;

      return {
        id: entity.id,
        hasPosition: entity.position !== undefined,
        hasPositionAtCurrentTime: position !== undefined,
        hasPoint: entity.point !== undefined,
        hasPath: entity.path !== undefined,
        ...(typeof pixelSize === "number" ? { pointPixelSize: pixelSize } : {}),
        ...(pointColorRgba ? { pointColorRgba } : {}),
        ...(typeof pathWidth === "number" ? { pathWidth } : {}),
        ...(position
          ? {
              positionAtCurrentTime: [
                position.x,
                position.y,
                position.z,
              ] as [number, number, number],
            }
          : {}),
      };
    });

    const viewerClock = this.viewer.clock;
    return {
      clock: {
        startTime: JulianDate.toIso8601(viewerClock.startTime, 3),
        stopTime: JulianDate.toIso8601(viewerClock.stopTime, 3),
        currentTime: JulianDate.toIso8601(viewerClock.currentTime, 3),
      },
      entities,
    };
  }
}

export class CesiumSceneManager {
  private readonly emptyDocument: CzmlPacket[];
  private dataSourcePort: CzmlDataSourcePort | undefined;
  private cameraController: CameraControllerPort | undefined;
  private sceneDocument: CzmlPacket[];
  private selectedEntityIds = new Set<string>();
  private initialized = false;
  private initialization: Promise<void> | undefined;
  private operationQueue: Promise<void> = Promise.resolve();

  constructor(
    emptyDocumentFactory: EmptyDocumentFactory = () =>
      createEmptyDocument(new Date()),
    dataSourcePort?: CzmlDataSourcePort,
    cameraController?: CameraControllerPort,
  ) {
    this.emptyDocument = cloneDocument(emptyDocumentFactory());
    this.sceneDocument = [];
    this.dataSourcePort = dataSourcePort;
    this.cameraController = cameraController;
  }

  initialize(viewer?: Viewer): Promise<void> {
    if (this.initialized) {
      return Promise.resolve();
    }
    if (this.initialization) {
      return this.initialization;
    }

    const initialization = this.initializeOnce(viewer)
      .then(() => {
        this.initialized = true;
      })
      .finally(() => {
        if (this.initialization === initialization) {
          this.initialization = undefined;
        }
      });
    this.initialization = initialization;
    return initialization;
  }

  private async initializeOnce(viewer?: Viewer): Promise<void> {
    if (!this.dataSourcePort) {
      if (!viewer) {
        throw new Error("A Cesium Viewer is required for initialization");
      }
      const cesiumPort = new CesiumCzmlDataSourcePort(viewer);
      this.dataSourcePort = cesiumPort;
      if (!this.cameraController) {
        this.cameraController = createCesiumCameraController(
          viewer,
          (id) => cesiumPort.getEntityById(id),
        );
      }
    }

    const empty = cloneDocument(this.emptyDocument);
    await this.dataSourcePort.load(cloneDocument(empty));
    this.sceneDocument = empty;
  }

  applySceneOps(operations: SceneOp[]): Promise<void> {
    const queuedOperations = structuredClone(operations);
    const application = this.operationQueue.then(() =>
      this.applySceneOpsInOrder(queuedOperations),
    );
    this.operationQueue = application.catch(() => undefined);
    return application;
  }

  private async applySceneOpsInOrder(operations: SceneOp[]): Promise<void> {
    await this.requireInitialization();
    const port = this.requireDataSourcePort();

    for (const operation of operations) {
      switch (operation.op) {
        case "clear": {
          // 先成功 load/提交 document，再清相机；load 失败保留原 track/orbit。
          const nextDocument = reduceSceneDocument(
            this.sceneDocument,
            [operation],
            this.emptyDocument,
          );
          await port.load(cloneDocument(nextDocument));
          this.sceneDocument = nextDocument;
          this.cameraController?.onSceneCleared();
          break;
        }
        case "upsert": {
          const businessPackets = lastPacketPerId(operation.packets);
          const previousDocument = cloneDocument(this.sceneDocument);
          const previousClock = clockFromDocument(previousDocument);
          const nextDocument = applyAvailabilityClock(
            reduceSceneDocument(
              this.sceneDocument,
              [{ op: "upsert", packets: businessPackets }],
              this.emptyDocument,
            ),
            businessPackets,
          );
          if (businessPackets.length > 0) {
            const replacedIds = businessPackets.map((packet) => packet.id);
            // load 会重建全部实体：先快照 tracked id，失败回滚后无条件重绑（即使更新 A 跟踪 B）。
            const trackedSnapshot =
              this.cameraController?.snapshotTrackedTargetId() ?? null;
            const replacementTx =
              this.cameraController?.beginEntityReplacement(replacedIds);
            const viewerClockSnapshot = port.snapshotViewerClock();
            try {
              for (const packet of businessPackets) {
                port.removeById(packet.id);
              }
              await port.process(cloneDocument(businessPackets));
              const nextClock = clockFromDocument(nextDocument);
              if (
                nextClock &&
                nextClock.interval !== previousClock?.interval
              ) {
                port.syncViewerClock(nextClock);
              }
              replacementTx?.commit();
            } catch (error) {
              try {
                await port.load(cloneDocument(previousDocument));
              } catch (loadError) {
                throw new AggregateError(
                  [error, loadError],
                  "CZML upsert failed and the prior Cesium document could not be restored",
                );
              }

              // load 成功后：restore 抛错也必须重绑；分别捕获并聚合全部错误。
              const secondaryErrors: unknown[] = [];
              try {
                port.restoreViewerClock(viewerClockSnapshot);
              } catch (restoreError) {
                secondaryErrors.push(restoreError);
              }
              try {
                this.cameraController?.rebindAfterReload(trackedSnapshot);
              } catch (rebindError) {
                secondaryErrors.push(rebindError);
              }
              if (secondaryErrors.length > 0) {
                throw new AggregateError(
                  [error, ...secondaryErrors],
                  "CZML upsert failed and the prior Cesium document could not be restored",
                );
              }
              throw error;
            }
          }
          this.sceneDocument = nextDocument;
          break;
        }
        case "delete":
          this.cameraController?.onEntitiesDeleted(operation.ids);
          for (const id of operation.ids) {
            port.removeById(id);
            this.sceneDocument = reduceSceneDocument(
              this.sceneDocument,
              [{ op: "delete", ids: [id] }],
              this.emptyDocument,
            );
          }
          break;
        case "style": {
          const previousDocument = cloneDocument(this.sceneDocument);
          const nextDocument = reduceSceneDocument(
            this.sceneDocument,
            [operation],
            this.emptyDocument,
          );
          const completePacket = nextDocument.find(
            (packet) => packet.id === operation.id,
          );
          if (!completePacket) {
            throw new Error(`样式目标实体不存在：'${operation.id}'。`);
          }

          const trackedSnapshot =
            this.cameraController?.snapshotTrackedTargetId() ?? null;
          const replacementTx =
            this.cameraController?.beginEntityReplacement([operation.id]);
          const viewerClockSnapshot = port.snapshotViewerClock();
          try {
            port.removeById(operation.id);
            await port.process(cloneDocument([completePacket]));
            replacementTx?.commit();
          } catch (error) {
            try {
              await port.load(cloneDocument(previousDocument));
            } catch (loadError) {
              throw new AggregateError(
                [error, loadError],
                "CZML style 失败且无法恢复先前的 Cesium document",
              );
            }

            // load 成功后：restore 抛错也必须重绑；分别捕获并聚合全部错误。
            const secondaryErrors: unknown[] = [];
            try {
              port.restoreViewerClock(viewerClockSnapshot);
            } catch (restoreError) {
              secondaryErrors.push(restoreError);
            }
            try {
              this.cameraController?.rebindAfterReload(trackedSnapshot);
            } catch (rebindError) {
              secondaryErrors.push(rebindError);
            }
            if (secondaryErrors.length > 0) {
              throw new AggregateError(
                [error, ...secondaryErrors],
                "CZML style 失败且无法恢复先前的 Cesium document",
              );
            }
            throw error;
          }

          this.sceneDocument = nextDocument;
          break;
        }
        case "camera":
          await this.requireCameraController().apply(operation);
          break;
        default:
          assertNever(operation, `未知 SceneOp：${JSON.stringify(operation)}`);
      }
    }
  }

  destroy(): void {
    this.cameraController?.destroy();
  }

  buildSummary(): SceneSummary {
    return buildSceneSummary(cloneDocument(this.sceneDocument));
  }

  pickRelevantPackets(ids: string[]): CzmlPacket[] {
    return selectRelevantPackets(this.sceneDocument, [...ids]);
  }

  setSelectedEntityIds(ids: string[]): void {
    this.selectedEntityIds = new Set(ids);
  }

  getSelectedEntityIds(): string[] {
    return [...this.selectedEntityIds];
  }

  getSceneDiagnostics(): SceneDiagnostics {
    const base = structuredClone(
      this.requireDataSourcePort().getSceneDiagnostics(),
    );
    const camera = this.cameraController?.getDiagnostics() ?? {
      trackedEntityId: null,
      orbitActive: false,
      orbitTargetId: null,
      orbitHeadingDegrees: null,
      headingDegrees: null,
      positionWC: null,
    };

    return {
      ...base,
      camera: structuredClone(camera),
      entities: base.entities.map((entity) => {
        const packet = this.sceneDocument.find(
          (candidate) => candidate.id === entity.id,
        );
        const position =
          packet != null &&
          "position" in packet &&
          packet.position != null &&
          typeof packet.position === "object"
            ? (packet.position as Record<string, unknown>)
            : undefined;
        const samples = Array.isArray(position?.cartesianVelocity)
          ? position.cartesianVelocity
          : Array.isArray(position?.cartesian)
            ? position.cartesian
            : undefined;
        return {
          ...entity,
          hasCanonicalPosition: position !== undefined,
          ...(samples
            ? { canonicalPositionSampleCount: samples.length }
            : {}),
        };
      }),
    };
  }

  private requireDataSourcePort(): CzmlDataSourcePort {
    if (!this.dataSourcePort || !this.initialized) {
      throw new Error("CesiumSceneManager must be initialized before use");
    }
    return this.dataSourcePort;
  }

  private requireCameraController(): CameraControllerPort {
    if (!this.cameraController) {
      throw new Error("相机控制器尚未初始化，无法执行 camera SceneOp。");
    }
    return this.cameraController;
  }

  private requireInitialization(): Promise<void> {
    if (this.initialized) {
      return Promise.resolve();
    }
    if (this.initialization) {
      return this.initialization;
    }
    throw new Error("CesiumSceneManager must be initialized before use");
  }
}
