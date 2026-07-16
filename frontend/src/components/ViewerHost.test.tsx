import { act, render, waitFor } from "@testing-library/react";
import type { Viewer } from "cesium";
import { vi } from "vitest";
import {
  ViewerHost,
  type ViewerSceneManager,
} from "./ViewerHost";

const cesiumFakes = vi.hoisted(() => {
  type SelectionListener = (entity?: { id?: string }) => void;

  const imagery = { name: "Natural Earth II" };
  const removeSelectionListener = vi.fn();
  const viewerInstances: Array<{
    container: Element | string;
    options: Record<string, unknown> | undefined;
    selectedEntityChanged: {
      addEventListener: ReturnType<typeof vi.fn>;
    };
    imageryLayers: {
      addImageryProvider: ReturnType<typeof vi.fn>;
    };
    destroy: ReturnType<typeof vi.fn>;
  }> = [];
  let selectionListener: SelectionListener | undefined;

  class FakeViewer {
    container: Element | string;
    options: Record<string, unknown> | undefined;
    selectedEntityChanged = {
      addEventListener: vi.fn((listener: SelectionListener) => {
        selectionListener = listener;
        return removeSelectionListener;
      }),
    };
    imageryLayers = {
      addImageryProvider: vi.fn(),
    };
    destroy = vi.fn();

    constructor(
      container: Element | string,
      options?: Record<string, unknown>,
    ) {
      this.container = container;
      this.options = options;
      viewerInstances.push(this);
    }
  }

  return {
    FakeViewer,
    imagery,
    viewerInstances,
    removeSelectionListener,
    getSelectionListener: () => selectionListener,
    fromUrl: vi.fn(async () => imagery),
    buildModuleUrl: vi.fn((path: string) => `/cesium/${path}`),
  };
});

vi.mock("cesium", () => ({
  Viewer: cesiumFakes.FakeViewer,
  TileMapServiceImageryProvider: {
    fromUrl: cesiumFakes.fromUrl,
  },
  buildModuleUrl: cesiumFakes.buildModuleUrl,
}));

function createManager(): ViewerSceneManager {
  return {
    initialize: vi.fn(async (_viewer: Viewer) => undefined),
    setSelectedEntityIds: vi.fn(),
  };
}

beforeEach(() => {
  cesiumFakes.viewerInstances.length = 0;
  cesiumFakes.removeSelectionListener.mockClear();
  cesiumFakes.fromUrl.mockClear();
  cesiumFakes.buildModuleUrl.mockClear();
});

it("creates one configured Viewer and initializes the manager on mount", async () => {
  const manager = createManager();

  const { container } = render(<ViewerHost sceneManager={manager} />);

  expect(cesiumFakes.viewerInstances).toHaveLength(1);
  const viewer = cesiumFakes.viewerInstances[0]!;
  expect(viewer.container).toBe(container.firstElementChild);
  expect(viewer.options).toMatchObject({
    animation: true,
    timeline: true,
    baseLayer: false,
    baseLayerPicker: false,
    geocoder: false,
  });
  expect(manager.initialize).toHaveBeenCalledOnce();
  expect(manager.initialize).toHaveBeenCalledWith(viewer);

  await waitFor(() => {
    expect(cesiumFakes.fromUrl).toHaveBeenCalledWith(
      "/cesium/Assets/Textures/NaturalEarthII",
    );
  });
  expect(viewer.imageryLayers.addImageryProvider).toHaveBeenCalledWith(
    cesiumFakes.imagery,
  );
});

it("does not create another Viewer when rerendered with the same manager", () => {
  const manager = createManager();
  const { rerender } = render(<ViewerHost sceneManager={manager} />);

  rerender(<ViewerHost sceneManager={manager} />);

  expect(cesiumFakes.viewerInstances).toHaveLength(1);
  expect(manager.initialize).toHaveBeenCalledOnce();
});

it("writes selection to the manager and cleans up the listener and Viewer", () => {
  const manager = createManager();
  const { unmount } = render(<ViewerHost sceneManager={manager} />);
  const viewer = cesiumFakes.viewerInstances[0]!;

  act(() => {
    cesiumFakes.getSelectionListener()?.({ id: "satellite" });
    cesiumFakes.getSelectionListener()?.();
  });

  expect(manager.setSelectedEntityIds).toHaveBeenNthCalledWith(1, [
    "satellite",
  ]);
  expect(manager.setSelectedEntityIds).toHaveBeenNthCalledWith(2, []);

  unmount();

  expect(cesiumFakes.removeSelectionListener).toHaveBeenCalledOnce();
  expect(viewer.destroy).toHaveBeenCalledOnce();
});
