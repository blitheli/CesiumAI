# CesiumAI

CesiumAI 是一个 React + Cesium 前端与 ASP.NET Core 后端组成的应用。用户通过自然语言清空场景、增改地面站、控制相机、修改实体样式，或通过 skill 驱动的通用传播器建星（含 ISS/SGP4 默认流程与保留的 J2 快捷路径）。后端 C# Tools 生成结构化 `sceneOps`；前端不会从助手文本中提取 CZML。

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

### 安装 astrox-skills（Git submodule）

`astrox-skills` 以 Git submodule 形式位于 `backend/astrox-skills`。Agent 从 `backend/astrox-skills/skills` 加载。

新 clone：

```bash
git clone --recurse-submodules <repo-url>
```

已有 clone：

```bash
git submodule update --init --recursive
```

若本地仍残留手动复制的 `backend/skills/`，可删除；默认配置不再使用该路径。

## 配置

### User Secrets（推荐）

不要把模型 key 写入仓库、README、`appsettings*.json` 或前端环境变量。为后端配置 .NET User Secrets：

```bash
dotnet user-secrets init --project backend/CesiumAI.Api
dotnet user-secrets set "Agent:ApiKey" "<your-key>" --project backend/CesiumAI.Api
```

仓库默认配置使用 `https://api.moonshot.cn/v1`、`kimi-k2.6`、`http://astrox.cn:8765` 和 `backend/astrox-skills/skills`。如需覆盖，可继续使用 User Secrets：

```bash
dotnet user-secrets set "Agent:Endpoint" "<openai-compatible-base-url>" --project backend/CesiumAI.Api
dotnet user-secrets set "Agent:Model" "<model-name>" --project backend/CesiumAI.Api
dotnet user-secrets set "Astrox:BaseUrl" "<astrox-base-url>" --project backend/CesiumAI.Api
dotnet user-secrets set "Skills:Path" "../astrox-skills/skills" --project backend/CesiumAI.Api
```

`Skills:Path` 必须是相对于 API content root（`backend/CesiumAI.Api`）的相对路径；默认 `../astrox-skills/skills` 指向 `backend/astrox-skills/skills`。

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
export Skills__Path="../astrox-skills/skills"
export VITE_API_BASE_URL="http://localhost:5088"
```

Shell 环境中的 key 只用于当前进程；不要把包含 key 的 shell 文件提交到 Git。`VITE_API_BASE_URL` 是公开的浏览器构建配置，绝不能用于存放 secret。

## 运行

### 一键启动（推荐）

先确保前端依赖已安装（见上文「安装」），再在仓库根目录安装根脚本依赖并启动：

```bash
npm install
npm run dev
```

该命令会同时启动：

- 后端 API：`http://localhost:5088`
- 前端 Vite：`http://localhost:5173`（自动设置 `VITE_API_BASE_URL=http://localhost:5088`）

打开 `http://localhost:5173`。按 `Ctrl+C` 会同时停止前后端。

`Agent:ApiKey` 仍通过 User Secrets 或环境变量 `Agent__ApiKey` 提供，不要写入 `package.json`。仅访问 `/healthz` 时可用占位 key 启动；真实对话需要有效 key。

单独启动：

```bash
npm run dev:api
npm run dev:web
```

### 分终端启动

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
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-Proto $scheme;
}
```

`frontend/dist/cesium` 是运行时必需的 Cesium 静态资源，部署时必须与其余 `dist/` 内容一起复制。

API 只接受一个 `X-Forwarded-Proto`，且只信任 `ReverseProxy:KnownProxies` 中的代理源 IP；默认值为同机 Nginx 使用的 `127.0.0.1` 和 `::1`。如果反代运行在独立容器或主机，必须替换为后端实际看到的代理源 IP，例如：

```bash
export ReverseProxy__KnownProxies__0="10.0.0.12"
```

不要加入不受控制的客户端网段或清空受信代理限制，否则客户端可伪造 scheme。Forwarded Headers Middleware 在 HTTPS redirect 前运行，因此受信代理传入的 `https` scheme 不会被再次重定向。

### publish 后的 skills 与启动验证

`Skills:Path` 在启动时仍相对于 API content root 解析，并且必须是相对路径。若从 `publish/api` 启动，可将 skills 放在相邻目录：

```bash
mkdir -p publish/skills
cp -R backend/astrox-skills/skills/. publish/skills/
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

`npm run e2e` 会自行启动 `http://127.0.0.1:5173` 上的 Vite，并拦截 `POST /api/chat`。Playwright 场景使用确定性响应，不访问 live LLM 或 Astrox；覆盖清空、添加/更新地面站、SSO/J2 卫星，以及相机定位/跟随/相对微调/单次与持续环绕/停止、ISS 样式修改后 Position 保留。验收通过**测试专用**只读 diagnostics（`VITE_ENABLE_TEST_DIAGNOSTICS=true`，由 Playwright `webServer` 注入；含 `data-scene-diagnostics` 与 `window.__CESIUM_AI_READ_DIAGNOSTICS__`）观测 tracked entity、orbit 状态、相机 heading/位置与样式，不绕过真实相机控制器。正常 `npm run build` / 生产构建不得设置该变量，因此不会暴露 diagnostics UI 或 window 全局。同时检查 Cesium canvas 持久存在且无 console error。

首次运行或 Playwright 升级后，如 Chromium 尚未安装：

```bash
cd frontend && npx playwright install chromium
```

## 自然语言能力与默认策略

### 相机

支持定位、跟随/停止跟随、相对缩放/平移/旋转、单次环绕与持续环绕/停止。相对旋转的 `headingDegrees`：**正为右转，负为左转**。示例：

```text
定位到地面站
跟随国际空间站
再拉近一点并向左转
绕国际空间站转一点
停止跟随并持续环绕
停止环绕
```

### 通用轨道传播

非 `AddSatelliteJ2` 快捷场景时，Agent 先加载对应 Astrox skill，再调用 `PropagateAndAddSatellite`。后端直接消费 Astrox 返回的标准 CZML `Position` 并写入 `upsert`；不得让大型 positions 在工具结果与模型参数间往返。也可用 `AddSatelliteFromPositions` 接入已有可信 Position。`AddSatelliteJ2` 仍保留以兼容现有调用。

### 国际空间站默认

用户仅说“添加国际空间站”时：

1. 先加载 SGP4/TLE 相关 skill
2. 使用受限 `HttpGet` 查询 NORAD Catalog Number `25544` 的最新 TLE
3. 调用专用 Tool `PropagateIssAndAddSatellite`（由 skill/TLE 构造 requestJson；服务端注入 `/Propagator/SGP4` 的 Start/Stop/Step，默认当前 UTC 截断到分钟起、未来 24 小时、步长 60 秒）

若 TLE 查询失败、结果不唯一或响应缺少两行根数，则禁止传播且不产生 `sceneOps`。用户明确指定时长或步长时可覆盖对应默认值。

### 实体样式

可通过自然语言修改已有实体的视觉属性；白名单顶层字段为 `point`、`path`、`label`、`billboard`、`model`、`polyline`、`polygon`、`ellipse`。禁止修改 `id`、`position`、`availability`、`properties` 或 `document`。示例：

```text
把国际空间站改成红色，轨迹宽度 5
```

## 手工验收 / 可选 live smoke

仅当模型凭据已配置、Astrox 可达且后端与前端均已启动时，依次输入：

```text
清空当前场景
添加一个地面站，经纬高是 -100, 30.2, 10
把该地面站高度改为 50 米
添加一个 900km SSO 卫星，使用 J2 递推一天
添加国际空间站
定位到地面站
跟随国际空间站
停止跟随并持续环绕
停止环绕
把国际空间站改成红色，轨迹宽度 5
```

确认 API 返回 typed `sceneOps`、助手文本只用于展示、前端不从文本提取 CZML，并且卫星轨迹随 Cesium 时钟动画显示。缺少凭据或 Astrox 不可达时，跳过 live smoke；自动化测试不需要这些外部服务。

## Git 安全

以下内容已被忽略，不应纳入 Git：

- `appsettings.Development.json`
- 前端依赖、构建和 Playwright 输出

`backend/astrox-skills` 为 Git submodule，其内容版本由 submodule commit 管理。提交前可运行 `git status --short`，确认没有 key、User Secrets 导出文件或误提交的密钥。
