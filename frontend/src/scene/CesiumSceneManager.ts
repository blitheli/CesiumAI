import { CzmlDataSource, JulianDate, type Viewer } from "cesium";
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
  syncViewerClock(): void;
}

export type EmptyDocumentFactory = () => CzmlPacket[];

function cloneDocument(document: CzmlPacket[]): CzmlPacket[] {
  return structuredClone(document);
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
      this.viewer.dataSources.add(this.dataSource);
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

  syncViewerClock(): void {
    const sourceClock = this.dataSource.clock;
    if (!sourceClock) {
      return;
    }

    const viewerClock = this.viewer.clock;
    viewerClock.startTime = JulianDate.clone(sourceClock.startTime);
    viewerClock.stopTime = JulianDate.clone(sourceClock.stopTime);
    viewerClock.currentTime = JulianDate.clone(sourceClock.currentTime);
    viewerClock.clockRange = sourceClock.clockRange;
    viewerClock.multiplier = sourceClock.multiplier;
    this.viewer.timeline.zoomTo(viewerClock.startTime, viewerClock.stopTime);
  }
}

export class CesiumSceneManager {
  private readonly emptyDocument: CzmlPacket[];
  private dataSourcePort: CzmlDataSourcePort | undefined;
  private sceneDocument: CzmlPacket[];
  private selectedEntityIds = new Set<string>();
  private initialization: Promise<void> | undefined;

  constructor(
    emptyDocumentFactory: EmptyDocumentFactory = () =>
      createEmptyDocument(new Date()),
    dataSourcePort?: CzmlDataSourcePort,
  ) {
    this.emptyDocument = cloneDocument(emptyDocumentFactory());
    this.sceneDocument = cloneDocument(this.emptyDocument);
    this.dataSourcePort = dataSourcePort;
  }

  initialize(viewer?: Viewer): Promise<void> {
    if (!this.initialization) {
      this.initialization = this.initializeOnce(viewer);
    }
    return this.initialization;
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

  async applySceneOps(operations: SceneOp[]): Promise<void> {
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
          const nextDocument = reduceSceneDocument(
            this.sceneDocument,
            [operation],
            this.emptyDocument,
          );
          await port.process(cloneDocument(operation.packets));
          port.syncViewerClock();
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

  private requireDataSourcePort(): CzmlDataSourcePort {
    if (!this.dataSourcePort || !this.initialization) {
      throw new Error("CesiumSceneManager must be initialized before use");
    }
    return this.dataSourcePort;
  }

  private requireInitialization(): Promise<void> {
    if (!this.initialization) {
      throw new Error("CesiumSceneManager must be initialized before use");
    }
    return this.initialization;
  }
}
