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
  positionAtCurrentTime?: [number, number, number];
};

export type SceneDiagnostics = {
  clock?: {
    startTime: string;
    stopTime: string;
    currentTime: string;
  };
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
      return {
        id: entity.id,
        hasPosition: entity.position !== undefined,
        hasPositionAtCurrentTime: position !== undefined,
        hasPoint: entity.point !== undefined,
        hasPath: entity.path !== undefined,
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
  private sceneDocument: CzmlPacket[];
  private selectedEntityIds = new Set<string>();
  private initialized = false;
  private initialization: Promise<void> | undefined;
  private operationQueue: Promise<void> = Promise.resolve();

  constructor(
    emptyDocumentFactory: EmptyDocumentFactory = () =>
      createEmptyDocument(new Date()),
    dataSourcePort?: CzmlDataSourcePort,
  ) {
    this.emptyDocument = cloneDocument(emptyDocumentFactory());
    this.sceneDocument = [];
    this.dataSourcePort = dataSourcePort;
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
      this.dataSourcePort = new CesiumCzmlDataSourcePort(viewer);
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
          const nextDocument = reduceSceneDocument(
            this.sceneDocument,
            [operation],
            this.emptyDocument,
          );
          await port.load(cloneDocument(nextDocument));
          this.sceneDocument = nextDocument;
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
            } catch (error) {
              try {
                await port.load(cloneDocument(previousDocument));
                port.restoreViewerClock(viewerClockSnapshot);
              } catch (rollbackError) {
                throw new AggregateError(
                  [error, rollbackError],
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
          for (const id of operation.ids) {
            port.removeById(id);
            this.sceneDocument = reduceSceneDocument(
              this.sceneDocument,
              [{ op: "delete", ids: [id] }],
              this.emptyDocument,
            );
          }
          break;
      }
    }
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
    return structuredClone(
      this.requireDataSourcePort().getSceneDiagnostics(),
    );
  }

  private requireDataSourcePort(): CzmlDataSourcePort {
    if (!this.dataSourcePort || !this.initialized) {
      throw new Error("CesiumSceneManager must be initialized before use");
    }
    return this.dataSourcePort;
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
