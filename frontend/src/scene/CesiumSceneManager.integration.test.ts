import {
  Clock,
  CzmlDataSource,
  JulianDate,
  type DataSource,
  type Viewer,
} from "cesium";
import { vi } from "vitest";
import { CesiumSceneManager } from "./CesiumSceneManager";

function createViewer() {
  const attached: DataSource[] = [];
  const viewer = {
    dataSources: {
      add: vi.fn(async (dataSource: DataSource) => {
        attached.push(dataSource);
        return dataSource;
      }),
    },
    clock: new Clock(),
    timeline: {
      zoomTo: vi.fn(),
    },
  };

  return { attached, viewer: viewer as unknown as Viewer };
}

it("applies complete facilities and dynamic satellites to a real CzmlDataSource", async () => {
  const { attached, viewer } = createViewer();
  const manager = new CesiumSceneManager(() => [
    {
      id: "document",
      version: "1.0",
      clock: {
        interval: "2035-01-01T00:00:00Z/2035-01-02T00:00:00Z",
        currentTime: "2035-01-01T00:00:00Z",
        multiplier: 60,
      },
    },
  ]);
  await manager.initialize(viewer);
  const dataSource = attached[0] as CzmlDataSource;

  await manager.applySceneOps([
    {
      op: "upsert",
      packets: [
        {
          id: "observable",
          position: { cartesian: [1, 2, 3] },
          point: { pixelSize: 10 },
          label: { text: "old-only-property" },
        },
      ],
    },
  ]);

  const facility = dataSource.entities.getById("observable");
  expect(facility?.position?.getValue(viewer.clock.currentTime)).toBeDefined();
  expect(facility?.point).toBeDefined();
  expect(manager.getSceneDiagnostics().entities).toEqual([
    expect.objectContaining({
      id: "observable",
      hasPoint: true,
      hasPosition: true,
      hasPositionAtCurrentTime: true,
    }),
  ]);

  const availability =
    "2026-01-01T00:00:00Z/2026-01-02T00:00:00Z";
  await manager.applySceneOps([
    {
      op: "upsert",
      packets: [
        {
          id: "observable",
          availability,
          position: {
            epoch: "2026-01-01T00:00:00Z",
            cartesianVelocity: [
              0, 7_271_000, 0, 0, 0, 1_000, 7_400,
              60, 7_270_000, 60_000, 444_000, -30, 999, 7_390,
            ],
          },
          path: { show: true, width: 2 },
        },
      ],
    },
  ]);

  const satellite = dataSource.entities.getById("observable");
  expect(satellite?.point).toBeUndefined();
  expect(satellite?.label).toBeUndefined();
  expect(satellite?.position).toBeDefined();
  expect(satellite?.path).toBeDefined();

  const start = JulianDate.fromIso8601("2026-01-01T00:00:00Z");
  const later = JulianDate.addSeconds(start, 30, new JulianDate());
  const startPosition = satellite?.position?.getValue(start);
  const laterPosition = satellite?.position?.getValue(later);
  expect(startPosition).toBeDefined();
  expect(laterPosition).toBeDefined();
  expect(laterPosition).not.toEqual(startPosition);

  const startDiagnostics = manager.getSceneDiagnostics();
  const diagnosticStartPosition =
    startDiagnostics.entities[0]?.positionAtCurrentTime;
  viewer.clock.currentTime = later;
  const laterDiagnostics = manager.getSceneDiagnostics();
  expect(laterDiagnostics.entities).toEqual([
    expect.objectContaining({
      id: "observable",
      hasPath: true,
      hasPoint: false,
      hasPosition: true,
      hasPositionAtCurrentTime: true,
    }),
  ]);
  expect(laterDiagnostics.entities[0]?.positionAtCurrentTime).not.toEqual(
    diagnosticStartPosition,
  );

  expect(JulianDate.lessThan(viewer.clock.currentTime, viewer.clock.startTime)).toBe(
    false,
  );
  expect(JulianDate.greaterThan(viewer.clock.currentTime, viewer.clock.stopTime)).toBe(
    false,
  );
  expect(startDiagnostics.clock?.currentTime).toBe(
    "2026-01-01T00:00:00.000Z",
  );
  expect(viewer.timeline.zoomTo).toHaveBeenLastCalledWith(
    viewer.clock.startTime,
    viewer.clock.stopTime,
  );

  await manager.applySceneOps([
    {
      op: "upsert",
      packets: [
        {
          id: "observable",
          position: { cartesian: [4, 5, 6] },
          point: { pixelSize: 12 },
        },
      ],
    },
  ]);
  const replacementFacility = dataSource.entities.getById("observable");
  expect(replacementFacility?.point).toBeDefined();
  expect(replacementFacility?.path).toBeUndefined();
  expect(manager.getSceneDiagnostics().entities).toEqual([
    expect.objectContaining({
      id: "observable",
      hasPath: false,
      hasPoint: true,
      hasPositionAtCurrentTime: true,
    }),
  ]);
});
