# CesiumAI 前端

React、TypeScript、Vite 与 CesiumJS 构成的 CesiumAI 产品页。前端持有完整 CZML 场景权威，通过单一长生命周期 `CzmlDataSource` 应用后端返回的 typed `sceneOps`。

```bash
npm ci
npm run dev
```

常用验证：

```bash
npm test -- --run
npm run typecheck
npm run lint
npm run build
npm run e2e
```

开发时用 `VITE_API_BASE_URL` 指向 ASP.NET API；生产环境推荐静态部署 `dist/`，并由同源反向代理转发 `/api`。完整的配置、skills 安装与部署说明见仓库根目录 [`README.md`](../README.md)。
