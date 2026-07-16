# CesiumAI

CesiumAI 是一个 React + Cesium 前端与 ASP.NET Core 后端组成的 MVP。用户通过自然语言清空场景、增改地面站，或调用 Astrox 生成 900 km SSO 卫星的一天 J2 星历。后端 C# Tools 生成结构化 `sceneOps`；前端不会从助手文本中提取 CZML。

## 前置条件

- .NET 10 SDK
- Node.js 20.19+ 或 22.12+
- npm
- 可访问的 OpenAI 兼容模型服务
- 可访问的 Astrox WebAPI

## 安装

安装前端依赖和 Playwright Chromium：

```bash
cd frontend
npm ci
npx playwright install chromium
cd ..
```

### 安装 astrox-skills（F3）

MVP 采用 PRD 决策 F3：手动将 astrox-skills 的 `skills/` 内容复制到 `backend/skills/`，由 `AgentSkillsProvider` 从该目录加载。它不是 git submodule，也不会被提交到本仓库。

```bash
git clone https://gitee.com/blitheli/astrox-skills.git /tmp/astrox-skills
mkdir -p backend/skills
cp -R /tmp/astrox-skills/skills/. backend/skills/
```

`backend/skills/` 已在 `.gitignore` 中。不要强制添加该目录。

## 配置

### User Secrets（推荐）

不要把模型 key 写入仓库、README、`appsettings*.json` 或前端环境变量。为后端配置 .NET User Secrets：

```bash
dotnet user-secrets init --project backend/CesiumAI.Api
dotnet user-secrets set "Agent:ApiKey" "<your-key>" --project backend/CesiumAI.Api
```

仓库默认配置使用 `https://api.moonshot.cn/v1`、`kimi-k2.6`、`http://astrox.cn:8765` 和 `backend/skills`。如需覆盖，可继续使用 User Secrets：

```bash
dotnet user-secrets set "Agent:Endpoint" "<openai-compatible-base-url>" --project backend/CesiumAI.Api
dotnet user-secrets set "Agent:Model" "<model-name>" --project backend/CesiumAI.Api
dotnet user-secrets set "Astrox:BaseUrl" "<astrox-base-url>" --project backend/CesiumAI.Api
dotnet user-secrets set "Skills:Path" "../skills" --project backend/CesiumAI.Api
```

`Skills:Path` 必须是相对于 API content root（`backend/CesiumAI.Api`）的相对路径；默认 `../skills` 指向 `backend/skills`。

### 环境变量替代方案

ASP.NET Core 用双下划线表示配置层级：

```text
Agent__ApiKey
Agent__Endpoint
Agent__Model
Astrox__BaseUrl
Skills__Path
VITE_API_BASE_URL
```

例如：

```bash
export Agent__ApiKey="<your-key>"
export Agent__Endpoint="https://api.moonshot.cn/v1"
export Agent__Model="kimi-k2.6"
export Astrox__BaseUrl="http://astrox.cn:8765"
export Skills__Path="../skills"
export VITE_API_BASE_URL="http://localhost:5088"
```

Shell 环境中的 key 只用于当前进程；不要把包含 key 的 shell 文件提交到 Git。`VITE_API_BASE_URL` 是公开的浏览器构建配置，绝不能用于存放 secret。

## 运行

终端 1：

```bash
dotnet run --project backend/CesiumAI.Api
```

开发 profile 默认在 `http://localhost:5088` 提供 API。

终端 2：

```bash
export VITE_API_BASE_URL="http://localhost:5088"
cd frontend && npm run dev
```

打开 `http://localhost:5173`。

## 生产部署

构建 API 与静态前端：

```bash
dotnet publish backend/CesiumAI.Api -c Release -o publish/api
cd frontend
npm ci
npm run build
cd ..
cp -R frontend/dist publish/frontend
```

生产构建不设置 `VITE_API_BASE_URL`，浏览器将同源请求 `/api/chat`。推荐由 Nginx、Caddy 或等价网关托管 `publish/frontend`，将 SPA 回退到 `index.html`，并把 `/api/` 与 `/healthz` 反向代理到只在内网监听的 ASP.NET 进程。例如 Nginx：

```nginx
location / {
    root /srv/cesiumai/frontend;
    try_files $uri $uri/ /index.html;
}

location /api/ {
    proxy_pass http://127.0.0.1:5088;
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-Proto $scheme;
}

location = /healthz {
    proxy_pass http://127.0.0.1:5088/healthz;
}
```

`frontend/dist/cesium` 是运行时必需的 Cesium 静态资源，部署时必须与其余 `dist/` 内容一起复制。

### publish 后的 skills 与启动验证

`Skills:Path` 在启动时仍相对于 API content root 解析，并且必须是相对路径。若从 `publish/api` 启动，可将 skills 放在相邻目录：

```bash
mkdir -p publish/skills
cp -R /tmp/astrox-skills/skills/. publish/skills/
cd publish/api
export Agent__ApiKey="<your-key>"
export Skills__Path="../skills"
export ASPNETCORE_URLS="http://127.0.0.1:5088"
dotnet CesiumAI.Api.dll
```

缺少目录、绝对 `Skills__Path` 或无效 Agent/Astrox 配置会在应用启动阶段直接失败，不会延迟到首个聊天请求。进程开始监听后，用健康端点验证启动与反代：

```bash
curl --fail http://127.0.0.1:5088/healthz
curl --fail https://cesiumai.example/healthz
```

两条命令均应返回 HTTP 200 和 `Healthy`。`/healthz` 只表示应用已通过启动配置验证并能够处理请求，不探测外部 LLM 或 Astrox。

## 验证和测试

在仓库根目录运行后端测试：

```bash
dotnet test CesiumAI.slnx
```

运行完整前端验证：

```bash
cd frontend
npm test -- --run
npm run typecheck
npm run build
npm run e2e
npm run lint
```

`npm run e2e` 会自行启动 `http://127.0.0.1:5173` 上的 Vite，并拦截 `POST /api/chat`。四个 Playwright 场景使用确定性响应，不访问 live LLM 或 Astrox；它们覆盖清空、添加地面站、更新地面站和添加 SSO/J2 卫星，同时直接检查实际 `CzmlDataSource` 的 position/point/path、Viewer availability 时钟与推进后的位置变化，并验证 `sceneSummary`、命名实体的 `relevantPackets`、持续存在的 Cesium canvas 和浏览器 console error。

首次运行或 Playwright 升级后，如 Chromium 尚未安装：

```bash
cd frontend && npx playwright install chromium
```

## 手工验收 / 可选 live smoke

仅当模型凭据已配置、Astrox 可达且后端与前端均已启动时，依次输入：

```text
清空当前场景
添加一个地面站，经纬高是 -100, 30.2, 10
把该地面站高度改为 50 米
添加一个 900km SSO 卫星，使用 J2 递推一天
```

确认 API 返回 typed `sceneOps`、助手文本只用于展示、前端不从文本提取 CZML，并且卫星轨迹随 Cesium 时钟动画显示。缺少凭据或 Astrox 不可达时，跳过 live smoke；自动化测试不需要这些外部服务。

## Git 安全

以下内容已被忽略，不应纳入 Git：

- `backend/skills/`
- `appsettings.Development.json`
- 前端依赖、构建和 Playwright 输出

提交前可运行 `git status --short`，确认没有 key、User Secrets 导出文件或 astrox-skills 内容。
