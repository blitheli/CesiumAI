import { createRoot } from "react-dom/client";
import { App } from "./app/App";
import { CesiumSceneManager } from "./scene/CesiumSceneManager";

const sceneManager = new CesiumSceneManager();
createRoot(document.getElementById("root")!).render(
  <App sceneManager={sceneManager} />,
);
