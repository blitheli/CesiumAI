import { fileURLToPath } from "node:url";
import { configDefaults, defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import { viteStaticCopy } from "vite-plugin-static-copy";
import { readLatestChangeDate } from "./build/changelogDate.ts";

const changesPath = fileURLToPath(new URL("../CHANGES.md", import.meta.url));

export default defineConfig({
  plugins: [
    react(),
    {
      name: "cesium-ai:changes-md-restart",
      // define 只在启动时求值；dev 期间修改 CHANGES.md 需重启才能刷新日期。
      configureServer(server) {
        server.watcher.add(changesPath);
        server.watcher.on("change", (file) => {
          if (file === changesPath) {
            void server.restart();
          }
        });
      },
    },
    viteStaticCopy({
      targets: ["Assets", "ThirdParty", "Widgets", "Workers"].map((name) => ({
        src: `node_modules/cesium/Build/Cesium/${name}`,
        dest: "cesium",
        rename: { stripBase: 4 },
      })),
    }),
  ],
  define: {
    CESIUM_BASE_URL: JSON.stringify("/cesium"),
    // 页面显示的更新日期唯一来源是仓库根目录 CHANGES.md 的最新条目。
    __APP_LAST_UPDATED__: JSON.stringify(readLatestChangeDate(changesPath)),
  },
  test: {
    globals: true,
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    css: true,
    exclude: [...configDefaults.exclude, "e2e/**"],
  },
});
