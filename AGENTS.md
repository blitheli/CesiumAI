# [AGENTS.md](http://AGENTS.md)


## 项目规范 
- 请始终使用**中文**与我进行沟通和回复。 
- 在编写代码注释、文档和提交信息时，请同样使用中文。
- 每次更新（每个 PR）都须在仓库根目录 `CHANGES.md` 追加条目：按日期倒序，日期用二级标题 `## YYYY-MM-DD`（同日改动合并到同一标题下），条目用中文简述改动并附 PR 链接。前端构建时会自动读取最新日期标题显示到页面副标题末尾（如 `用自然语言探索和编辑场景-20261006`），无需手工修改前端代码；缺少日期标题或日期非法会导致前端构建失败。


## Cursor Cloud 专用说明

CesiumAI 是双层应用：ASP.NET Core（.NET 10）后端 API 与 React + Cesium（Vite）前端，通过 `POST /api/chat` 连接。标准安装、运行、测试与部署命令见 `README.md` 和 `frontend/package.json`，优先使用那些说明。下文仅记录在本环境中运行时的非显而易见注意点。

### 工具链 / 环境

- 环境由 Cursor Dashboard 中保存的 `install` 脚本准备（不依赖基础快照预装）：幂等安装 `.NET 10 SDK` 到 `/usr/local/dotnet` 并软链到 `/usr/local/bin/dotnet`；确保 nvm 下的 Node.js 22 并软链 `node`/`npm`/`npx` 到 `/usr/local/bin`；初始化 skills submodule；在根目录与 `frontend/` 执行 `npm ci`；安装 Playwright Chromium；执行 `dotnet build CesiumAI.slnx` 预热 NuGet。
- 若 `dotnet` 不存在（例如环境尚未从新 build 启动），手动执行：`curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh && sudo mkdir -p /usr/local/dotnet && sudo chown "$(id -u):$(id -g)" /usr/local/dotnet && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/local/dotnet --no-path && sudo ln -sf /usr/local/dotnet/dotnet /usr/local/bin/dotnet`。
- `backend/astrox-skills` 为 Git submodule（上游 `https://github.com/blitheli/astrox-skills.git`）。构建时同步到 API content root 内的 `backend/CesiumAI.Api/skills/`（默认 `Skills:Path=skills`）。若 submodule 未初始化，构建/启动会失败；在仓库根目录执行：`git submodule sync --recursive && git submodule update --init --recursive`。

### 一键启动（推荐）

- 仓库根目录：`npm install && npm run dev`。会同时启动后端 `:5088` 与前端 `:5173`，并自动设置 `VITE_API_BASE_URL=http://localhost:5088`。
- 也可单独：`npm run dev:api` / `npm run dev:web`。

### 运行后端（`http://localhost:5088`）

- 在仓库根目录执行：`dotnet run --project backend/CesiumAI.Api`。
- 启动使用 `ValidateOnStart`：若 `Agent:ApiKey` 为空、`Agent:Endpoint`/`Astrox:BaseUrl` 不是绝对 HTTP(S) URL，或 skills 目录不存在，会立即失败。通过环境变量 `Agent__ApiKey=...`（或 User Secrets）提供 key。占位 key 足以启动并访问 `/healthz`（返回 `Healthy`），但不足以进行真实对话。
- `/healthz` 与启动过程不会调用 LLM 或 Astrox。只有实际的 `POST /api/chat` 请求才会访问外部 OpenAI 兼容 LLM（默认 `api.moonshot.cn`）和 Astrox。因此真实对话需要有效的 `Agent:ApiKey`；没有时 `/api/chat` 返回 HTTP 500（`HTTP 401 invalid_authentication_error`）——其余管线已验证可用。

### 运行前端（`http://localhost:5173`）

- `cd frontend && npm run dev`。设置 `VITE_API_BASE_URL=http://localhost:5088`，让浏览器跨域调用后端（开发环境 CORS 允许 `http://localhost:5173`）。未设置时，前端会请求同源的 `/api/chat`。

### 测试（无需外部服务）

- 后端：`dotnet test CesiumAI.slnx`。
- 前端：`npm test -- --run`（单元测试）、`npm run lint`（oxlint）、`npm run typecheck`、`npm run build`、`npm run e2e`（Playwright）。e2e 会在 `:5173` 自行启动 Vite 并 mock `POST /api/chat`，因此不需要后端、LLM 或 Astrox。`npm run e2e` 约需 2 分钟。

