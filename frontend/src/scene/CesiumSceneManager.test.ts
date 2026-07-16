import type { Viewer } from "cesium";
import { vi } from "vitest";
import type { CzmlPacket } from "../contracts/chat";
import {
  CesiumSceneManager,
  type CzmlDataSourcePort,
} from "./CesiumSceneManager";

const cesiumFakes = vi.hoisted(() => {
  const dataSources: Array<{
    load: ReturnType<typeof vi.fn>;
    process: ReturnType<typeof vi.fn>;
    entities: { removeById: ReturnType<typeof vi.fn> };
    clock?: {
      startTime: { value: string };
      stopTime: { value: string };
      currentTime: { value: string };
      clockRange: number;
      multiplier: number;
    };
  }> = [];

  class FakeCzmlDataSource {
    load = vi.fn(async () => this);
    process = vi.fn(async () => this);
    entities = { removeById: vi.fn(() => true) };
    clock:
      | {
          startTime: { value: string };
          stopTime: { value: string };
          currentTime: { value: string };
          clockRange: number;
          multiplier: number;
        }
      | undefined;

    constructor(_name: string) {
      dataSources.push(this);
    }
  }

  return {
    dataSources,
    FakeCzmlDataSource,
    clone: vi.fn((value: { value: string }) => ({ ...value })),
  };
});

vi.mock("cesium", () => ({
  CzmlDataSource: cesiumFakes.FakeCzmlDataSource,
  JulianDate: { clone: cesiumFakes.clone },
}));

function createEmpty(): CzmlPacket[] {
  return [
    {
      id: "document",
      name: "Test scene",
      version: "1.0",
      clock: {
        interval: "2026-07-16T00:00:00Z/2026-07-17T00:00:00Z",
        currentTime: "2026-07-16T00:00:00Z",
      },
    },
  ];
}

function createPort(overrides: Partial<CzmlDataSourcePort> = {}): CzmlDataSourcePort {
  return {
    load: vi.fn(async () => undefined),
    process: vi.fn(async () => undefined),
    removeById: vi.fn(() => true),
    syncViewerClock: vi.fn(),
    ...overrides,
  };
}

beforeEach(() => {
  cesiumFakes.dataSources.length = 0;
  cesiumFakes.clone.mockClear();
});

it("loads the empty document exactly once during initialization", async () => {
  const port = createPort();
  const manager = new CesiumSceneManager(createEmpty, port);

  await manager.initialize();

  expect(port.load).toHaveBeenCalledOnce();
  expect(port.load).toHaveBeenCalledWith(createEmpty());
});

it("waits for initialization before applying the first operation", async () => {
  let finishLoading: (() => void) | undefined;
  const loading = new Promise<void>((resolve) => {
    finishLoading = resolve;
  });
  const port = createPort({
    load: vi.fn(() => loading),
  });
  const manager = new CesiumSceneManager(createEmpty, port);

  const initializing = manager.initialize();
  const applying = manager.applySceneOps([
    { op: "upsert", packets: [{ id: "satellite", path: {} }] },
  ]);

  await Promise.resolve();
  expect(port.process).not.toHaveBeenCalled();

  finishLoading?.();
  await initializing;
  await applying;
  expect(port.process).toHaveBeenCalledOnce();
});

it("routes clear, upsert, and delete through the port in operation order", async () => {
  const calls: string[] = [];
  const port = createPort({
    load: vi.fn(async () => {
      calls.push("load");
    }),
    process: vi.fn(async () => {
      calls.push("process");
    }),
    removeById: vi.fn((id) => {
      calls.push(`remove:${id}`);
      return true;
    }),
    syncViewerClock: vi.fn(() => {
      calls.push("sync-clock");
    }),
  });
  const manager = new CesiumSceneManager(createEmpty, port);
  await manager.initialize();
  calls.length = 0;

  await manager.applySceneOps([
    { op: "upsert", packets: [{ id: "satellite", path: {} }] },
    { op: "delete", ids: ["satellite"] },
    { op: "clear" },
  ]);

  expect(calls).toEqual([
    "process",
    "sync-clock",
    "remove:satellite",
    "load",
  ]);
  expect(port.process).toHaveBeenCalledWith([
    { id: "satellite", path: {} },
  ]);
  expect(port.load).toHaveBeenLastCalledWith(createEmpty());
});

it("filters document packets before processing a mixed upsert", async () => {
  const port = createPort();
  const manager = new CesiumSceneManager(createEmpty, port);
  await manager.initialize();

  await manager.applySceneOps([
    {
      op: "upsert",
      packets: [
        { id: "document", name: "Untrusted document" },
        { id: "facility", point: {} },
      ],
    },
  ]);

  expect(port.process).toHaveBeenCalledOnce();
  expect(port.process).toHaveBeenCalledWith([{ id: "facility", point: {} }]);
  expect(manager.pickRelevantPackets(["document", "facility"])).toEqual([
    ...createEmpty(),
    { id: "facility", point: {} },
  ]);
});

it("does not process an upsert containing only document packets", async () => {
  const port = createPort();
  const manager = new CesiumSceneManager(createEmpty, port);
  await manager.initialize();

  await manager.applySceneOps([
    {
      op: "upsert",
      packets: [{ id: "document", name: "Untrusted document" }],
    },
  ]);

  expect(port.process).not.toHaveBeenCalled();
  expect(port.syncViewerClock).not.toHaveBeenCalled();
  expect(manager.pickRelevantPackets(["document"])).toEqual(createEmpty());
});

it("commits an upsert only after Cesium processing and clock sync succeed", async () => {
  let resolveProcess: (() => void) | undefined;
  const processPending = new Promise<void>((resolve) => {
    resolveProcess = resolve;
  });
  const port = createPort({
    process: vi.fn(() => processPending),
  });
  const manager = new CesiumSceneManager(createEmpty, port);
  await manager.initialize();

  const applying = manager.applySceneOps([
    { op: "upsert", packets: [{ id: "facility", point: {} }] },
  ]);

  expect(manager.pickRelevantPackets(["facility"])).toEqual([]);
  expect(port.syncViewerClock).not.toHaveBeenCalled();

  resolveProcess?.();
  await applying;

  expect(port.syncViewerClock).toHaveBeenCalledOnce();
  expect(manager.pickRelevantPackets(["facility"])).toEqual([
    { id: "facility", point: {} },
  ]);
});

it("stops at a rejected operation and leaves its document change uncommitted", async () => {
  const failure = new Error("CZML rejected");
  const port = createPort({
    process: vi.fn(async () => {
      throw failure;
    }),
  });
  const manager = new CesiumSceneManager(createEmpty, port);
  await manager.initialize();

  await expect(
    manager.applySceneOps([
      { op: "upsert", packets: [{ id: "invalid", point: {} }] },
      { op: "clear" },
    ]),
  ).rejects.toBe(failure);

  expect(port.syncViewerClock).not.toHaveBeenCalled();
  expect(port.load).toHaveBeenCalledOnce();
  expect(manager.pickRelevantPackets(["invalid"])).toEqual([]);
});

it("commits successful operations before a later operation rejects", async () => {
  const failure = new Error("second packet rejected");
  const port = createPort({
    process: vi
      .fn()
      .mockResolvedValueOnce(undefined)
      .mockRejectedValueOnce(failure),
  });
  const manager = new CesiumSceneManager(createEmpty, port);
  await manager.initialize();

  await expect(
    manager.applySceneOps([
      { op: "upsert", packets: [{ id: "committed", point: {} }] },
      { op: "upsert", packets: [{ id: "rejected", point: {} }] },
      { op: "delete", ids: ["committed"] },
    ]),
  ).rejects.toBe(failure);

  expect(manager.pickRelevantPackets(["committed", "rejected"])).toEqual([
    { id: "committed", point: {} },
  ]);
  expect(port.removeById).not.toHaveBeenCalled();
});

it("serializes concurrent apply calls so later work uses committed state", async () => {
  let finishFirstProcess: (() => void) | undefined;
  const firstProcess = new Promise<void>((resolve) => {
    finishFirstProcess = resolve;
  });
  const port = createPort({
    process: vi
      .fn()
      .mockImplementationOnce(() => firstProcess)
      .mockResolvedValueOnce(undefined),
  });
  const manager = new CesiumSceneManager(createEmpty, port);
  await manager.initialize();

  const first = manager.applySceneOps([
    {
      op: "upsert",
      packets: [{ id: "facility", name: "first", point: {} }],
    },
  ]);
  const second = manager.applySceneOps([
    {
      op: "upsert",
      packets: [{ id: "facility", name: "second", point: {} }],
    },
  ]);
  await Promise.resolve();
  await Promise.resolve();

  expect(port.process).toHaveBeenCalledOnce();

  finishFirstProcess?.();
  await Promise.all([first, second]);

  expect(port.process).toHaveBeenNthCalledWith(1, [
    { id: "facility", name: "first", point: {} },
  ]);
  expect(port.process).toHaveBeenNthCalledWith(2, [
    { id: "facility", name: "second", point: {} },
  ]);
  expect(manager.pickRelevantPackets(["facility"])).toEqual([
    { id: "facility", name: "second", point: {} },
  ]);
});

it("continues queued apply calls after an earlier call rejects", async () => {
  const failure = new Error("first apply failed");
  let rejectFirstProcess: ((reason: Error) => void) | undefined;
  const firstProcess = new Promise<void>((_resolve, reject) => {
    rejectFirstProcess = reject;
  });
  const port = createPort({
    process: vi
      .fn()
      .mockImplementationOnce(() => firstProcess)
      .mockResolvedValueOnce(undefined),
  });
  const manager = new CesiumSceneManager(createEmpty, port);
  await manager.initialize();

  const first = manager.applySceneOps([
    { op: "upsert", packets: [{ id: "failed", point: {} }] },
  ]);
  const firstResult = first.then(
    () => undefined,
    (error: unknown) => error,
  );
  const second = manager.applySceneOps([
    { op: "upsert", packets: [{ id: "recovered", point: {} }] },
  ]);
  await Promise.resolve();
  await Promise.resolve();

  expect(port.process).toHaveBeenCalledOnce();

  rejectFirstProcess?.(failure);
  expect(await firstResult).toBe(failure);
  await second;

  expect(port.process).toHaveBeenCalledTimes(2);
  expect(manager.pickRelevantPackets(["failed", "recovered"])).toEqual([
    { id: "recovered", point: {} },
  ]);
});

it("returns detached summary, packet, and selection values", async () => {
  const manager = new CesiumSceneManager(createEmpty, createPort());
  await manager.initialize();
  await manager.applySceneOps([
    { op: "upsert", packets: [{ id: "facility", name: "Sanya", point: {} }] },
  ]);
  manager.setSelectedEntityIds(["facility"]);

  const packets = manager.pickRelevantPackets(["facility"]);
  const selected = manager.getSelectedEntityIds();
  const summary = manager.buildSummary();
  packets[0]!.name = "mutated";
  selected.push("mutated");
  summary.entities[0]!.name = "mutated";

  expect(manager.pickRelevantPackets(["facility"])[0]?.name).toBe("Sanya");
  expect(manager.getSelectedEntityIds()).toEqual(["facility"]);
  expect(manager.buildSummary().entities[0]?.name).toBe("Sanya");
});

it("production port loads before attaching and syncs cloned data-source clock values", async () => {
  const viewer = {
    dataSources: {
      add: vi.fn(),
    },
    clock: {
      startTime: { value: "old-start" },
      stopTime: { value: "old-stop" },
      currentTime: { value: "old-current" },
      clockRange: 0,
      multiplier: 1,
    },
    timeline: {
      zoomTo: vi.fn(),
    },
  };
  const manager = new CesiumSceneManager(createEmpty);

  await manager.initialize(viewer as unknown as Viewer);
  const dataSource = cesiumFakes.dataSources[0]!;

  dataSource.clock = {
    startTime: { value: "start" },
    stopTime: { value: "stop" },
    currentTime: { value: "current" },
    clockRange: 2,
    multiplier: 60,
  };
  await manager.applySceneOps([
    { op: "upsert", packets: [{ id: "satellite", path: {} }] },
  ]);

  expect(dataSource.load).toHaveBeenCalledOnce();
  expect(dataSource.load.mock.invocationCallOrder[0]).toBeLessThan(
    viewer.dataSources.add.mock.invocationCallOrder[0]!,
  );
  expect(viewer.clock).toEqual({
    startTime: { value: "start" },
    stopTime: { value: "stop" },
    currentTime: { value: "current" },
    clockRange: 2,
    multiplier: 60,
  });
  expect(viewer.clock.startTime).not.toBe(dataSource.clock.startTime);
  expect(viewer.clock.stopTime).not.toBe(dataSource.clock.stopTime);
  expect(viewer.clock.currentTime).not.toBe(dataSource.clock.currentTime);
  expect(viewer.timeline.zoomTo).toHaveBeenCalledWith(
    viewer.clock.startTime,
    viewer.clock.stopTime,
  );
});

it("waits for the data source attachment before completing initialization", async () => {
  let finishAttaching: (() => void) | undefined;
  const attaching = new Promise<void>((resolve) => {
    finishAttaching = resolve;
  });
  const viewer = {
    dataSources: {
      add: vi.fn(() => attaching),
    },
    clock: {},
    timeline: {},
  };
  const manager = new CesiumSceneManager(createEmpty);

  const initializing = manager.initialize(viewer as unknown as Viewer);
  const applying = manager.applySceneOps([
    { op: "upsert", packets: [{ id: "facility", point: {} }] },
  ]);
  await Promise.resolve();
  await Promise.resolve();

  const dataSource = cesiumFakes.dataSources[0]!;
  expect(viewer.dataSources.add).toHaveBeenCalledOnce();
  expect(manager.pickRelevantPackets(["document"])).toEqual([]);
  expect(dataSource.process).not.toHaveBeenCalled();

  finishAttaching?.();
  await initializing;
  await applying;

  expect(manager.pickRelevantPackets(["document", "facility"])).toEqual([
    ...createEmpty(),
    { id: "facility", point: {} },
  ]);
});

it("keeps initialization uncommitted after attachment failure and allows retry", async () => {
  const failure = new Error("viewer rejected data source");
  const viewer = {
    dataSources: {
      add: vi
        .fn()
        .mockRejectedValueOnce(failure)
        .mockResolvedValueOnce(undefined),
    },
    clock: {},
    timeline: {},
  };
  const manager = new CesiumSceneManager(createEmpty);

  await expect(
    manager.initialize(viewer as unknown as Viewer),
  ).rejects.toBe(failure);
  expect(manager.pickRelevantPackets(["document"])).toEqual([]);

  await manager.initialize(viewer as unknown as Viewer);

  const dataSource = cesiumFakes.dataSources[0]!;
  expect(dataSource.load).toHaveBeenCalledTimes(2);
  expect(viewer.dataSources.add).toHaveBeenCalledTimes(2);
  expect(manager.pickRelevantPackets(["document"])).toEqual(createEmpty());
});
