import { configDefaults, defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import { viteStaticCopy } from "vite-plugin-static-copy";

export default defineConfig({
  plugins: [
    react(),
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
  },
  test: {
    globals: true,
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    css: true,
    exclude: [...configDefaults.exclude, "e2e/**"],
  },
});
