# [AGENTS.md](http://AGENTS.md)


## 项目规范 
- 请始终使用**中文**与我进行沟通和回复。 
- 在编写代码注释、文档和提交信息时，请同样使用中文。


## Cursor Cloud 专用说明

CesiumAI 是双层应用：ASP.NET Core（.NET 10）后端 API 与 React + Cesium（Vite）前端，通过 `POST /api/chat` 连接。标准安装、运行、测试与部署命令见 `README.md` 和 `frontend/package.json`，优先使用那些说明。下文仅记录在本环境中运行时的非显而易见注意点。

### 工具链 / 环境

- `.NET 10 SDK` 安装在 `/usr/local/dotnet`，并软链接到 `/usr/local/bin/dotnet`（已在 `PATH` 中）。Node.js 22 与 npm 已预装。这些已写入 VM 快照；启动更新脚本仅刷新 npm 依赖与 Playwright Chromium。
- `backend/astrox-skills` 为 Git submodule（上游 `https://gitee.com/blitheli/astrox-skills.git`）。后端从 `backend/astrox-skills/skills` 加载 skills（默认 `Skills:Path=../astrox-skills/skills`）。若 submodule 未初始化，后端会在启动时快速失败；在仓库根目录执行：`git submodule update --init --recursive`。

### 运行后端（`http://localhost:5088`）

- 在仓库根目录执行：`dotnet run --project backend/CesiumAI.Api`。
- 启动使用 `ValidateOnStart`：若 `Agent:ApiKey` 为空、`Agent:Endpoint`/`Astrox:BaseUrl` 不是绝对 HTTP(S) URL，或 skills 目录不存在，会立即失败。通过环境变量 `Agent__ApiKey=...`（或 User Secrets）提供 key。占位 key 足以启动并访问 `/healthz`（返回 `Healthy`），但不足以进行真实对话。
- `/healthz` 与启动过程不会调用 LLM 或 Astrox。只有实际的 `POST /api/chat` 请求才会访问外部 OpenAI 兼容 LLM（默认 `api.moonshot.cn`）和 Astrox。因此真实对话需要有效的 `Agent:ApiKey`；没有时 `/api/chat` 返回 HTTP 500（`HTTP 401 invalid_authentication_error`）——其余管线已验证可用。

### 运行前端（`http://localhost:5173`）

- `cd frontend && npm run dev`。设置 `VITE_API_BASE_URL=http://localhost:5088`，让浏览器跨域调用后端（开发环境 CORS 允许 `http://localhost:5173`）。未设置时，前端会请求同源的 `/api/chat`。

### 测试（无需外部服务）

- 后端：`dotnet test CesiumAI.slnx`。
- 前端：`npm test -- --run`（单元测试）、`npm run lint`（oxlint）、`npm run typecheck`、`npm run build`、`npm run e2e`（Playwright）。e2e 会在 `:5173` 自行启动 Vite 并 mock `POST /api/chat`，因此不需要后端、LLM 或 Astrox。`npm run e2e` 约需 2 分钟。

