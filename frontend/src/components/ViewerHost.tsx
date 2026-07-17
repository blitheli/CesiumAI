import { useEffect, useRef } from "react";
import {
  TileMapServiceImageryProvider,
  Viewer,
  buildModuleUrl,
  type Entity,
} from "cesium";
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

    const viewer = new Viewer(container, {
      animation: true,
      timeline: true,
      baseLayer: false,
      baseLayerPicker: false,
      fullscreenButton: false,
      vrButton: false,
      geocoder: false,
      homeButton: false,
      infoBox: false,
      sceneModePicker: false,
      selectionIndicator: false,
      navigationHelpButton: false,
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
        const imagery = await TileMapServiceImageryProvider.fromUrl(
          buildModuleUrl("Assets/Textures/NaturalEarthII"),
        );
        if (!disposed) {
          viewer.imageryLayers.addImageryProvider(imagery);
        }
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
