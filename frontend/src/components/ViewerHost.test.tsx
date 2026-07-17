import { act, render, waitFor } from "@testing-library/react";
import type { Viewer } from "cesium";
import { beforeEach, vi } from "vitest";
import { ViewerHost } from "./ViewerHost";

const cesiumFakes = vi.hoisted(() => {
  type SelectionListener = (entity?: { id?: string }) => void;

  const removeSelectionListener = vi.fn();
  const viewerInstances: Array<{
    container: Element | string;
    options: Record<string, unknown> | undefined;
    selectedEntityChanged: {
      addEventListener: ReturnType<typeof vi.fn>;
    };
    destroy: ReturnType<typeof vi.fn>;
  }> = [];
  let selectionListener: SelectionListener | undefined;
  let ionToken: string | undefined;

  class FakeViewer {
    container: Element | string;
    options: Record<string, unknown> | undefined;
    selectedEntityChanged = {
      addEventListener: vi.fn((listener: SelectionListener) => {
        selectionListener = listener;
        return removeSelectionListener;
      }),
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
    viewerInstances,
    removeSelectionListener,
    getSelectionListener: () => selectionListener,
    Ion: {
      get defaultAccessToken(): string | undefined {
        return ionToken;
      },
      set defaultAccessToken(value: string | undefined) {
        ionToken = value;
      },
    },
    resetIonToken: () => {
      ionToken = undefined;
    },
  };
});

vi.mock("cesium", () => ({
  Viewer: cesiumFakes.FakeViewer,
  Ion: cesiumFakes.Ion,
}));

function createManager() {
  return {
    initialize: vi.fn(async (_viewer: Viewer) => undefined),
    setSelectedEntityIds: vi.fn(),
    destroy: vi.fn(),
  };
}

const originalIonToken = import.meta.env.VITE_CESIUM_ION_TOKEN;

beforeEach(() => {
  cesiumFakes.viewerInstances.length = 0;
  cesiumFakes.removeSelectionListener.mockClear();
  cesiumFakes.resetIonToken();
  if (originalIonToken === undefined) {
    delete (import.meta.env as { VITE_CESIUM_ION_TOKEN?: string })
      .VITE_CESIUM_ION_TOKEN;
  } else {
    (
      import.meta.env as { VITE_CESIUM_ION_TOKEN?: string }
    ).VITE_CESIUM_ION_TOKEN = originalIonToken;
  }
});

it("creates one Viewer with typical widgets and initializes the manager", async () => {
  const manager = createManager();

  const { container } = render(<ViewerHost sceneManager={manager} />);

  expect(cesiumFakes.viewerInstances).toHaveLength(1);
  const viewer = cesiumFakes.viewerInstances[0]!;
  expect(viewer.container).toBe(container.firstElementChild);
  expect(viewer.options).toMatchObject({
    animation: true,
    timeline: true,
    baseLayerPicker: true,
    fullscreenButton: true,
    geocoder: true,
    homeButton: true,
    infoBox: true,
    sceneModePicker: true,
    selectionIndicator: true,
    navigationHelpButton: true,
    vrButton: false,
  });
  expect(viewer.options).not.toHaveProperty("baseLayer");
  expect(manager.initialize).toHaveBeenCalledOnce();
  expect(manager.initialize).toHaveBeenCalledWith(viewer);

  await waitFor(() => {
    expect(manager.initialize).toHaveBeenCalled();
  });
});

it("sets Ion.defaultAccessToken when VITE_CESIUM_ION_TOKEN is non-empty", () => {
  (
    import.meta.env as { VITE_CESIUM_ION_TOKEN?: string }
  ).VITE_CESIUM_ION_TOKEN = "test-ion-token";
  const manager = createManager();

  render(<ViewerHost sceneManager={manager} />);

  expect(cesiumFakes.Ion.defaultAccessToken).toBe("test-ion-token");
});

it("does not set Ion token when VITE_CESIUM_ION_TOKEN is empty", () => {
  (
    import.meta.env as { VITE_CESIUM_ION_TOKEN?: string }
  ).VITE_CESIUM_ION_TOKEN = "";
  const manager = createManager();

  render(<ViewerHost sceneManager={manager} />);

  expect(cesiumFakes.Ion.defaultAccessToken).toBeUndefined();
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
  expect(manager.destroy).toHaveBeenCalledOnce();
  expect(viewer.destroy).toHaveBeenCalledOnce();
  expect(manager.destroy.mock.invocationCallOrder[0]).toBeLessThan(
    viewer.destroy.mock.invocationCallOrder[0]!,
  );
});
