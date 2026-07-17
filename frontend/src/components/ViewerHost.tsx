import { useEffect, useRef } from "react";
import { Ion, Viewer, type Entity } from "cesium";
import "cesium/Build/Cesium/Widgets/widgets.css";

export interface ViewerSceneManager {
  initialize(viewer: Viewer): Promise<void>;
  setSelectedEntityIds(ids: string[]): void;
  destroy(): void;
}

export type ViewerHostProps = {
  sceneManager: ViewerSceneManager;
};

export function ViewerHost({ sceneManager }: ViewerHostProps) {
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const container = containerRef.current;
    if (!container) {
      return;
    }

    const ionToken = import.meta.env.VITE_CESIUM_ION_TOKEN;
    if (typeof ionToken === "string" && ionToken.trim().length > 0) {
      Ion.defaultAccessToken = ionToken;
    }

    const viewer = new Viewer(container, {
      animation: true,
      timeline: true,
      baseLayerPicker: true,
      fullscreenButton: true,
      vrButton: false,
      geocoder: true,
      homeButton: true,
      infoBox: true,
      sceneModePicker: true,
      selectionIndicator: true,
      navigationHelpButton: true,
    });
    let disposed = false;

    const removeSelectionListener =
      viewer.selectedEntityChanged.addEventListener(
        (selectedEntity?: Entity) => {
          sceneManager.setSelectedEntityIds(
            selectedEntity?.id ? [selectedEntity.id] : [],
          );
        },
      );

    const initialize = async () => {
      try {
        await sceneManager.initialize(viewer);
      } catch (error) {
        if (!disposed) {
          console.error("Failed to initialize the Cesium Viewer", error);
        }
      }
    };

    void initialize();

    return () => {
      disposed = true;
      removeSelectionListener();
      sceneManager.destroy();
      viewer.destroy();
    };
  }, [sceneManager]);

  return <div ref={containerRef} aria-label="Cesium globe" />;
}
