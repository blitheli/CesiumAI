# Design: astrox-skills 改为 Git Submodule（F3 → F1）

日期：2026-07-16  
状态：待用户审阅

## 背景

当前 MVP 采用 PRD 决策 F3：手动把 `https://gitee.com/blitheli/astrox-skills.git` 的 `skills/` 复制到 `backend/skills/`，并在 `.gitignore` 中忽略该目录。云端或新机器 clone 主仓后没有 skills，后端启动会因 `Skills:Path` 目录不存在而失败。

目标：升级为 F1（Git submodule），使 `git clone --recurse-submodules` / `git submodule update --init` 即可获得 skills，本地与云端行为一致。

## 决策

| 项 | 选择 |
|---|---|
| Submodule 路径 | `backend/astrox-skills` |
| Skills 运行时路径 | `backend/astrox-skills/skills` |
| 默认 `Skills:Path` | `../astrox-skills/skills`（相对 API content root） |
| 发布产物 | 仍复制到 `publish/skills`，运行时 `Skills__Path=../skills` |
| 提交策略 | 本变更准备好后由用户决定是否 commit；不自动 push |

不采用 `backend/skills` 作为 submodule 根路径，避免形成 `backend/skills/skills` 的歧义结构。

## 变更范围

### Git

1. 添加 submodule：
   - URL：`https://gitee.com/blitheli/astrox-skills.git`
   - 路径：`backend/astrox-skills`
   - 固定当前上游 `HEAD`（或添加时解析到的 commit）
2. 生成/更新 `.gitmodules`
3. 从 `.gitignore` 删除 `backend/skills/`
4. 若本地仍有手动复制的 `backend/skills/`，迁移说明中提示删除或移走，避免与旧路径混淆；不强制在仓库内保留兼容别名

### 配置与代码

1. `backend/CesiumAI.Api/appsettings.json`：`Skills:Path` → `../astrox-skills/skills`
2. `SkillsOptions` 默认值同步为 `../astrox-skills/skills`
3. 受默认路径影响的测试（如 `AgentFactoryTests`）更新为新相对路径
4. 不改变 `SkillsOptions` 的相对路径校验与启动失败行为

### 文档

1. `README.md`：安装步骤改为 submodule；去掉 F3 手动复制；发布段改为从 `backend/astrox-skills/skills` 复制到 `publish/skills`
2. `Docs/prd.md`：Skills 接入从 F3 标记为已升级到 F1；后续扩展中“F3 → F1”项标记完成或删除
3. 可选：在 `Docs/superpowers/plans/2026-07-16-cesiumai-mvp.md` 中用简短说明标注已由本设计 superseded（不重写整份历史计划）

### 非目标

- 不改 Agent / Tool / Astrox HTTP 行为
- 不引入 CI 工作流文件（除非仓库已有 CI；当前不新增）
- 不把 skills 内容 vendoring 进主仓 blob
- 不自动 commit / push

## 开发者与部署流程

### 新 clone

```bash
git clone --recurse-submodules <repo-url>
```

### 已有 clone

```bash
git submodule update --init --recursive
```

### 本地运行

默认配置即可解析到 `backend/astrox-skills/skills`。User Secrets / 环境变量仍可覆盖 `Skills:Path`。

### Publish

```bash
mkdir -p publish/skills
cp -R backend/astrox-skills/skills/. publish/skills/
# 在 publish/api 下运行时：
# Skills__Path=../skills
```

发布目录不依赖 Git submodule 运行时存在，只依赖构建阶段已初始化 submodule。

## 验收标准

1. `.gitmodules` 存在，且 `backend/astrox-skills` 指向 gitee 上游
2. `git submodule status` 显示已 checkout 的 commit
3. `dotnet test`（后端测试项目）通过
4. 在 submodule 已初始化前提下，默认 `Skills:Path` 能解析到存在的目录
5. README 不再要求手动 `cp` 到 `backend/skills/`，并说明 recurse-submodules
6. `.gitignore` 不再忽略 `backend/skills/`（旧手动目录若存在则仅为本地残留，不进仓）

## 风险与对策

| 风险 | 对策 |
|---|---|
| 忘记 `--recurse-submodules` | README 明确；启动时 DirectoryNotFound 错误已存在且清晰 |
| gitee 不可达 | 与当前 F3 相同外部依赖；固定 commit 至少保证版本可复现 |
| 本地仍有旧 `backend/skills` | 文档提示删除；默认配置不再指向该路径 |
| 用户已设 User Secret `Skills:Path=../skills` | README 提示更新为 `../astrox-skills/skills` 或删除覆盖 |

## 实施顺序（摘要）

1. 添加 submodule，更新 `.gitignore`
2. 改默认配置与测试
3. 更新 README / PRD
4. 运行后端测试验证
5. 由用户审阅 diff 后决定是否 commit
