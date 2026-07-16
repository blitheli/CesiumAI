/// <reference types="vite/client" />

import type { SceneDiagnostics } from "./scene/CesiumSceneManager";

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string;
  /** 仅测试/验收启用只读 diagnostics；生产构建不得设为 true。 */
  readonly VITE_ENABLE_TEST_DIAGNOSTICS?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

declare global {
  interface Window {
    /** 只读场景诊断读取器；仅在 VITE_ENABLE_TEST_DIAGNOSTICS=true 时挂载。 */
    __CESIUM_AI_READ_DIAGNOSTICS__?: () => SceneDiagnostics;
  }
}

export {};
