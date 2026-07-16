import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: "list",
  workers: 1,
  timeout: 60_000,
  use: {
    baseURL: "http://127.0.0.1:5173",
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
  webServer: {
    command:
      "npm run dev -- --host 127.0.0.1 --port 5173 --strictPort",
    url: "http://127.0.0.1:5173",
    // 始终由本配置拉起带 VITE_ENABLE_TEST_DIAGNOSTICS 的 Vite，禁止复用缺诊断变量的已有服务。
    reuseExistingServer: false,
    timeout: 120_000,
    env: {
      ...process.env,
      // 仅 Playwright 验收启用只读 diagnostics；正常 npm run build 不设置。
      VITE_ENABLE_TEST_DIAGNOSTICS: "true",
    },
  },
});
