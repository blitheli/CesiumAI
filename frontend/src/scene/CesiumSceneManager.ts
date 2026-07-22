/**
 * Cesium 场景中枢：维护内存 CZML 文档与球上 `CzmlDataSource` 的一致性，
 * 并串行应用后端下发的 `sceneOps`（clear / upsert / delete / style / camera）。
 *
 * 职责概览（详见 Docs/前端说明.md §4.6）：
 * - `sceneDocument`：内存中的 CZML packet 数组（含 `document`）
 * - `CzmlDataSource`：挂到 Viewer 上的数据源（经 `CzmlDataSourcePort` 抽象，便于测试注入）
 * - `CameraController`：解释 `camera` ops
 * - 操作队列：`applySceneOps` 串行执行，避免并发打乱文档
 *
 * `upsert` / `style` 失败时会尝试回滚到先前文档并恢复时钟/跟踪，
 * 避免球上状态与内存文档长期不一致。
 */
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

/** 对 Cesium `CzmlDataSource` + Viewer 时钟的可测试抽象。 */
export interface CzmlDataSourcePort {
  /** 
   * 全量加载 packets（替换 DataSource 内实体集合）。
   * 用于：initialize 空文档、clear、upsert/style 失败后的回滚。
   * 首次调用时把本 DataSource 挂到 Viewer；之后只 `load` 内容。
   */
  load(packets: CzmlPacket[]): Promise<unknown>;

  /** 增量处理 packets（upsert / style 的实体替换路径）。 */
  process(packets: CzmlPacket[]): Promise<unknown>;
  removeById(id: string): boolean;
  /** 用 document packet 的 clock 同步 Viewer 时间轴。 */
  syncViewerClock(clock: CzmlDocumentClock): void;
  snapshotViewerClock(): ViewerClockSnapshot;
  restoreViewerClock(snapshot: ViewerClockSnapshot): void;
  /** 只读诊断：时钟与实体在当前时刻的可视化状态。 */
  getSceneDiagnostics(): SceneDiagnostics;
}

export type EmptyDocumentFactory = () => CzmlPacket[];

/** document packet 中的时钟字段（ISO8601 interval + currentTime）。 */
export type CzmlDocumentClock = {
  interval: string;
  currentTime: string;
  multiplier?: number;
};

/** Viewer 时钟快照，用于 upsert/style 失败后的精确恢复。 */
export type ViewerClockSnapshot = {
  startTime: JulianDate;
  stopTime: JulianDate;
  currentTime: JulianDate;
  clockRange: ClockRange;
  multiplier: number;
  shouldAnimate: boolean;
};

/** 单个实体的只读诊断信息（测试 / 调试用）。 */
export type SceneEntityDiagnostics = {
  id: string;
  hasPosition: boolean;
  hasPositionAtCurrentTime: boolean;
  hasPoint: boolean;
  hasPath: boolean;
  /** 内存 canonical 文档中是否含 position。 */
  hasCanonicalPosition?: boolean;
  /** canonical packet 中 Position 采样点数量（只读，用于样式后保留校验）。 */
  canonicalPositionSampleCount?: number;
  positionAtCurrentTime?: [number, number, number];
  pointPixelSize?: number;
  pointColorRgba?: [number, number, number, number];
  pathWidth?: number;
};

/** 场景只读诊断：Viewer 时钟、相机与实体状态。 */
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

/** 从 document packet 读取时钟；字段不完整时返回 undefined。 */
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

/**
 * 若 upsert 的业务 packet 带有 `availability`（区间串），
 * 则把 document 的 clock.interval / currentTime 对齐到该窗口，
 * 便于时间轴覆盖实体有效期。
 */
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

/** 返回去重后的 packets，同一 id 多次出现时只保留最后一次（忽略 document）。 */
function lastPacketPerId(packets: CzmlPacket[]): CzmlPacket[] {
  const packetsById = new Map<string, CzmlPacket>();
  for (const packet of packets) {
    if (packet.id !== "document") {
      packetsById.set(packet.id, packet);
    }
  }
  return [...packetsById.values()];
}

/**
 * `CzmlDataSourcePort` 的真实 Cesium 实现。
 *
 * 职责：把 SceneManager 的「改球」意图落到 Viewer 上——
 * 持有一个名为 `"scene"` 的 `CzmlDataSource`，负责实体 load/process/删除，
 * 以及 Viewer 时钟与时间轴的同步 / 快照 / 恢复。
 *
 * 与内存 `sceneDocument` 的分工：
 * - 文档权威在 `CesiumSceneManager.sceneDocument`
 * - 本类只反映「球上当前应显示什么」；回滚时由 SceneManager 再 `load` 旧文档
 *
 * 生命周期：在 `initialize(viewer)` 时创建；首次 `load` 时把 DataSource
 * 加入 `viewer.dataSources`（挂载），之后 clear/回滚继续用同一实例 `load`。
 */
class CesiumCzmlDataSourcePort implements CzmlDataSourcePort {
  /** 宿主 Viewer：挂 DataSource、读写 clock / timeline。 */
  private readonly viewer: Viewer;
  /** 场景专用 CZML 数据源（name = "scene"），承载全部业务实体。 */
  private readonly dataSource: CzmlDataSource;
  /** 是否已执行过 `viewer.dataSources.add`；保证只挂载一次。 */
  private attached = false;

  constructor(viewer: Viewer) {
    this.viewer = viewer;
    this.dataSource = new CzmlDataSource("scene");
  }

  /**
   * 按 id 取CzmlDataSource上实体（供相机控制器 focus/track 解析目标）。
   * 不在 `CzmlDataSourcePort` 接口上，仅本实现额外暴露给 SceneManager。
   */
  getEntityById(id: string) {
    return this.dataSource.entities.getById(id);
  }

  /**
   * 全量加载 packets（替换 DataSource 内实体集合）。
   * 用于：initialize 空文档、clear、upsert/style 失败后的回滚。
   * 首次调用时把本 DataSource 挂到 Viewer；之后只 `load` 内容。
   */
  async load(packets: CzmlPacket[]): Promise<unknown> {
    // 可以只传某一个或几个 packet 的数组给 Cesium——API 支持。
    // 但若用 load，等于「Viewer 球上只剩你这次传入的这些」；其它实体会被清掉。
    // 要「只更新某几个、保留其余」，应走 process（本项目 upsert/style 已是这条路径）。
    const result = await this.dataSource.load(packets);

    if (!this.attached) {
      await this.viewer.dataSources.add(this.dataSource);
      this.attached = true;
    }
    return result;
  }

  /**
   * 增量处理 packets（Cesium `CzmlDataSource.process`）。是按属性增量合并。
   * 用于 upsert/style：通常先 `removeById` 再 process 新完整包，避免 id 冲突残留。
   */
  process(packets: CzmlPacket[]): Promise<unknown> {
    // 增量处理 packets（Cesium `CzmlDataSource.process`）。
    // 用于 upsert/style：通常先 `removeById` 再 process 新完整包，避免 id 冲突残留。
    return this.dataSource.process(packets);
  }

  /** 从 DataSource 移除指定实体；返回是否原先存在。 */
  removeById(id: string): boolean {
    return this.dataSource.entities.removeById(id);
  }

  /**
   * 用 document.clock 同步 Viewer 时钟与时间轴。
   * `interval` 形如 `startIso/stopIso`；若 currentTime 越界则夹到 start。
   * 时钟范围固定为 LOOP_STOP；有 multiplier 则一并写入。
   */
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

  /**
   * 快照当前 Viewer 时钟（含是否在动画）。
   * upsert/style 开始前调用，失败回滚时配合 `restoreViewerClock`。
   */
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

  /** 从快照恢复 Viewer 时钟与时间轴缩放（回滚路径）。 */
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

  /**
   * 只读诊断：当前时刻各实体是否有 position/point/path 及采样值，
   * 加上 Viewer 时钟 ISO 字符串。不含内存 canonical 文档字段
   * （那些由 `CesiumSceneManager.getSceneDiagnostics` 再合并）。
   */
  getSceneDiagnostics(): SceneDiagnostics {
    const currentTime = this.viewer.clock.currentTime;
    const entities = this.dataSource.entities.values.map((entity) => {
      const position = entity.position?.getValue(currentTime);
      const pixelSize = entity.point?.pixelSize?.getValue(currentTime);
      const color = entity.point?.color?.getValue(currentTime);
      const pathWidth = entity.path?.width?.getValue(currentTime);
      // Cesium Color 为 0..1 浮点；诊断输出为 0..255 整数 rgba，便于对照 CZML。
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

/**
 * 场景中枢：把后端 `sceneOps` 落到内存 CZML 文档与 Cesium Viewer。
 *
 * 公开 API：
 * - `initialize(viewer)` — 挂 DataSource、相机控制器，加载空文档
 * - `applySceneOps(ops)` — 按序应用 clear/upsert/delete/style/camera
 * - `buildSummary()` — 生成发给后端的场景摘要
 * - `pickRelevantPackets(ids)` — 按 id 取完整 packet
 * - `setSelectedEntityIds` / `getSelectedEntityIds` — 与 Viewer 选中同步
 * - `getSceneDiagnostics()` — 只读诊断（测试开关下可用）
 * - `destroy()` — 释放相机控制器等资源
 *
 * 构造时可注入 `dataSourcePort` / `cameraController`，便于单测绕过真实 Cesium。
 */
export class CesiumSceneManager {
  /** 空场景模板（仅 document packet）；clear 时归约到此副本。 */
  private readonly emptyDocument: CzmlPacket[];
  private dataSourcePort: CzmlDataSourcePort | undefined;
  private cameraController: CameraControllerPort | undefined;
  /** 内存中的权威 CZML 文档（含 document + 业务实体）。 */
  private sceneDocument: CzmlPacket[];
  /** 当前选中实体 id，供摘要/相关 packet 推断使用。 */
  private selectedEntityIds = new Set<string>();
  private initialized = false;
  /** 进行中的初始化 Promise，避免并发重复 initialize。 */
  private initialization: Promise<void> | undefined;
  /**
   * 操作串行队列：后一次 `applySceneOps` 挂在前一次之后，
   * 防止并发修改打乱 `sceneDocument` 与球上状态。
   */
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

  /**
   * 挂载 DataSource 与相机控制器，并加载空文档到。
   * 
   * 1. 挂载 DataSource(dataSourcePort) 与相机控制器(cameraController)
   * 2. 加载空文档
   * 3. 设置初始化标志
   * 4. 返回初始化 Promise
   * 
   * Promise<void> 表示：一个异步操作的 Promise，成功时没有有意义的返回值。
   * await sceneManager.initialize(viewer); // 等它结束即可，左边一般不接变量
   */
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
    /**
     * 1. 如果 dataSourcePort 不存在，则创建一个新的 CesiumCzmlDataSourcePort
     * 2. 如果 viewer 不存在，则抛出错误
     * 3. 创建一个新的 CesiumCzmlDataSourcePort
     * 4. 如果 cameraController 不存在，则创建一个新的 createCesiumCameraController
     * 5. 返回初始化 Promise
     * 6. 加载空文档
     * 7. 设置 sceneDocument
     */
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
    // 加载空文档
    await this.dataSourcePort.load(cloneDocument(empty));
    this.sceneDocument = empty;
  }

  /**
   * 按序应用一批 SceneOp。调用会入队串行执行；入队前深拷贝 ops，避免外部后续修改影响队列中的快照。
   * 队列项失败会被吞掉以便后续 ops 仍可排队，但本次 Promise 仍会 reject。
   */
  applySceneOps(operations: SceneOp[]): Promise<void> {
    const queuedOperations = structuredClone(operations);
    const application = this.operationQueue.then(() =>
      this.applySceneOpsInOrder(queuedOperations),
    );
    this.operationQueue = application.catch(() => undefined);
    return application;
  }

  /**
   * 实际串行应用逻辑：文档归约在 `reduceSceneDocument`，相机 ops 交给 CameraController。
   * upsert/style 失败时 load 回滚先前文档并恢复时钟与跟踪。
   */
  private async applySceneOpsInOrder(operations: SceneOp[]): Promise<void> {
    await this.requireInitialization();
    // 确保 DataSourcePort 已初始化
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
          // 业务 packet 去重后归约内存文档；失败则回滚球上状态与时钟/跟踪。
          // 同一 id 多次出现时只保留最后一次（忽略 document）。
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
          // 先在内存归约出完整 packet，再替换球上实体；失败同样回滚。
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
          // 相机不改文档，单独交给相机控制器。
          await this.requireCameraController().apply(operation);
          break;
        default:
          assertNever(operation, `未知 SceneOp：${JSON.stringify(operation)}`);
      }
    }
  }

  /** 释放相机控制器等资源（Viewer 生命周期由 ViewerHost 负责）。 */
  destroy(): void {
    this.cameraController?.destroy();
  }

  /** 生成发给后端的轻量场景摘要（不含全量 CZML）。 */
  buildSummary(): SceneSummary {
    return buildSceneSummary(cloneDocument(this.sceneDocument));
  }

  /** 按实体 id 从内存文档取出完整 packet，用于请求体的 relevantPackets。 */
  pickRelevantPackets(ids: string[]): CzmlPacket[] {
    return selectRelevantPackets(this.sceneDocument, [...ids]);
  }

  /** 与 Viewer 选中同步，供后续相关实体推断。 */
  setSelectedEntityIds(ids: string[]): void {
    this.selectedEntityIds = new Set(ids);
  }

  getSelectedEntityIds(): string[] {
    return [...this.selectedEntityIds];
  }

  /**
   * 只读诊断：Viewer/DataSource 状态 + 相机诊断 + canonical 文档中的 position 元数据。
   * 主要用于测试与调试，不参与业务路径。
   */
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

  // 确保 DataSourcePort 已初始化
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
      //马上创建一个「已成功」的空 Promise，调用方 await 会立刻继续
      return Promise.resolve();
    }
    if (this.initialization) {
      return this.initialization;
    }
    throw new Error("CesiumSceneManager must be initialized before use");
  }
}
