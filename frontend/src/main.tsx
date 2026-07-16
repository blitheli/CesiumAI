import { createRoot } from "react-dom/client";
import { ViewerHost } from "./components/ViewerHost";
import "./index.css";
import { CesiumSceneManager } from "./scene/CesiumSceneManager";

const sceneManager = new CesiumSceneManager();
createRoot(document.getElementById("root")!).render(
  <ViewerHost sceneManager={sceneManager} />,
);
