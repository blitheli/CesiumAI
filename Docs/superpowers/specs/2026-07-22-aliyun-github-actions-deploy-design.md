# 阿里云 GitHub Actions 前后端部署设计

日期：2026-07-22  
状态：已实现

## 背景

生产环境已在阿里云 Windows Server + IIS 建好站点目录，并在 GitHub 仓库配置了 SSH 登录用 Secret。需要通过 GitHub Actions 在推送到 `main` 时分别编译打包前端/后端，经 OpenSSH 上传到服务器对应目录；后端部署后重启 IIS 应用池以加载新程序集。

同时统一 skills 布局：运行时不再使用与 API「并列」的 `../astrox-skills/skills` 或 `../skills`，改为始终在 API content root **内部**的 `skills/` 目录。

## 目标

- 推送到 `main` 且相关路径变更时自动部署
- 前端、后端各一份独立 workflow
- 使用已有 Secret：`ALIYUN_HOST`、`ALIYUN_USERNAME`、`ALIYUN_PASSWORD`
- 不把 API Key 等敏感配置写入仓库或 Actions 明文
- 默认 `Skills:Path=skills`；本地 `dotnet run` / `dotnet publish` / IIS 站点均从 content root 下的 `skills/` 加载
- **服务器无需再单独配置 `Skills__Path`**（除非故意覆盖）

## 非目标

- 不配置/修改服务器上的 IIS 站点、反代、证书
- 不通过 Actions 注入 `Agent__ApiKey`（假定已在 IIS/环境变量中配置）
- 不移动 Git submodule 物理位置（仍保留 `backend/astrox-skills` 作为上游拉取源）
- 不引入 Docker / 蓝绿发布
- 不自动跑 e2e / 完整测试套件作为部署门禁（可后续加）

## 为什么以前要配 `Skills__Path`？

旧布局是「API 目录」与「skills 目录」并列：

```text
# 开发
backend/CesiumAI.Api/          ← content root
backend/astrox-skills/skills/  ← 并列，默认 Path=../astrox-skills/skills

# 旧发布建议
publish/api/
publish/skills/                ← 并列，需 Skills__Path=../skills
```

路径相对 content root，发布后目录结构一变就要用环境变量改路径。  
改为 skills **打进** API 目录后，开发和生产结构一致，默认 `skills` 即可，服务器不用再配。

## Skills 统一布局（范围扩展）

### 运行时约定

| 环境 | content root | skills 目录 | `Skills:Path` |
|------|--------------|-------------|---------------|
| 本地 `dotnet run` | `backend/CesiumAI.Api/` | `backend/CesiumAI.Api/skills/` | `skills` |
| `dotnet publish` 输出 | 发布目录 | `{publish}/skills/` | `skills` |
| IIS | `D:/IIS/ASTROX.CesiumAI.backend/` | `D:/IIS/ASTROX.CesiumAI.backend/skills/` | `skills` |

### 源码与构建

- Git submodule **仍在** `backend/astrox-skills`（只作版本化来源，不作为运行时路径）
- 在 `CesiumAI.Api.csproj` 中把 `..\astrox-skills\skills\**\*` 复制到输出/发布目录的 `skills\`（`CopyToOutputDirectory` + `CopyToPublishDirectory`）
- `appsettings.json` 与 `SkillsOptions` 默认值改为 `skills`
- 更新相关测试、README、AGENTS.md 中的路径说明
- 若本机 User Secrets 仍覆盖为 `../astrox-skills/skills`，需删除该覆盖，否则会盖过新默认值

## 约束与约定

| 项 | 值 |
|----|-----|
| 触发分支 | `main` |
| 传输 | OpenSSH（密码认证） |
| 前端目标目录 | `D:/IIS/ASTROX.CesiumAI.frontend` |
| 后端目标目录 | `D:/IIS/ASTROX.CesiumAI.backend` |
| 后端应用池 | `CesiumAI.backend` |
| 路径过滤 | 前端 `frontend/**`；后端 `backend/**` |
| 默认 Skills 路径 | `skills`（站点/项目内部） |

## 方案选择

采用**两个独立 workflow**（非单文件双 job）：职责清晰，与「分别创建前后端 Actions」一致。

## Workflow 设计

### 文件

```
.github/workflows/deploy-frontend.yml
.github/workflows/deploy-backend.yml
```

### 前端 `deploy-frontend.yml`

1. `on.push.branches: [main]`，`paths: ['frontend/**']`
2. `actions/checkout`
3. `actions/setup-node`（Node 22），`cache: npm`，`cache-dependency-path: frontend/package-lock.json`
4. `cd frontend && npm ci && npm run build`  
   - **不设置** `VITE_API_BASE_URL`（生产同源 `/api`）
5. 使用 `appleboy/scp-action` 将 `frontend/dist/*` 上传到 `D:/IIS/ASTROX.CesiumAI.frontend`  
   - `rm: true`（或等价清理策略）避免残留旧静态资源；目标路径严格限定为站点目录
6. 不重启应用池

### 后端 `deploy-backend.yml`

1. `on.push.branches: [main]`，`paths: ['backend/**']`
2. `actions/checkout`，`submodules: recursive`（拉取 `astrox-skills`）
3. `actions/setup-dotnet`（.NET 10）
4. `dotnet publish backend/CesiumAI.Api -c Release -o publish/api`  
   - csproj 已把 skills 复制进 `publish/api/skills/`，**无需**额外手工 `cp` 到并列目录
5. `appleboy/ssh-action`：`Stop-WebAppPool -Name 'CesiumAI.backend'`
6. `appleboy/scp-action`：上传 `publish/api/*`（含内部 `skills/`）到 `D:/IIS/ASTROX.CesiumAI.backend`
7. `appleboy/ssh-action`：`Start-WebAppPool -Name 'CesiumAI.backend'`

### Secret 使用

所有 SSH/SCP 步骤仅通过 GitHub Secrets 读取主机与凭据，不写入日志明文密码。

## 文档

在 `README.md`「生产部署」与配置说明中：

- 改为默认 `Skills:Path=skills`（内部目录）
- 删除「publish 旁并列 skills + `Skills__Path=../skills`」的旧说明
- 补充两个 workflow 的触发条件、目标路径、Secrets、应用池名称
- 提醒：OpenSSH Server 已开启；若曾设置旧的 `Skills__Path` / User Secrets，请删除以免覆盖

## 风险与缓解

| 风险 | 缓解 |
|------|------|
| Windows 路径 / OpenSSH 路径分隔符 | SCP target 使用 `D:/IIS/...` 正斜杠 |
| 上传时 DLL 被占用 | 先 Stop-WebAppPool 再 SCP，完成后 Start |
| `rm: true` 误删站点外文件 | target 严格限定为站点物理路径 |
| 本地仍用旧 User Secrets 路径 | README/AGENTS 明确提示删除旧覆盖 |
| submodule 未 init 导致 publish 缺 skills | workflow 使用 `submodules: recursive`；本地需 `git submodule update --init` |
| 密码登录 OpenSSH | 与现有 Secret 方案一致；后续可升级为 SSH key |

## 验收标准

- 默认配置下，`dotnet run --project backend/CesiumAI.Api` 从 `CesiumAI.Api/skills`（由构建复制）启动成功
- `dotnet publish` 输出目录内存在 `skills/`，且无需设置 `Skills__Path`
- 仅改 `frontend/` 推 `main` → 只跑前端 workflow
- 仅改 `backend/` 推 `main` → 只跑后端 workflow，应用池先停后启；站点内为 `.../backend/skills/`
- `/healthz` 在部署后仍返回 `Healthy`（需服务器已正确配置 Agent/Astrox）
- 仓库内无明文服务器密码或 API Key
