# astrox-skills Git Submodule Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 `astrox-skills` 从 F3 手动复制改为 F1 Git submodule，使 clone / CI 可复现获得 skills。

**Architecture:** 在 `backend/astrox-skills` 添加指向 `https://gitee.com/blitheli/astrox-skills.git` 的 submodule；默认 `Skills:Path` 改为 `../astrox-skills/skills`；发布时仍复制到 `publish/skills`，运行时用 `../skills`。

**Tech Stack:** Git submodule、.NET 配置（`SkillsOptions` / `appsettings.json`）、xUnit 测试、Markdown 文档。

**Spec:** `Docs/superpowers/specs/2026-07-16-astrox-skills-submodule-design.md`

## Global Constraints

- Submodule 路径必须是 `backend/astrox-skills`（不是 `backend/skills`）。
- 运行时 skills 根目录是 `backend/astrox-skills/skills`。
- `Skills:Path` 必须保持相对 API content root；不得改为绝对路径。
- 不改 Agent / Tool / Astrox HTTP 行为。
- 不自动 `git commit` / `git push`，除非用户明确要求。
- Windows PowerShell 环境下命令用等价语法；文档示例可保留 bash。

---

## File Map

| 文件 | 职责 |
|---|---|
| `.gitmodules` | submodule 声明 |
| `backend/astrox-skills/` | submodule checkout（含 `skills/`） |
| `.gitignore` | 删除旧 `backend/skills/` ignore |
| `backend/CesiumAI.Api/Configuration/SkillsOptions.cs` | 默认相对路径 |
| `backend/CesiumAI.Api/appsettings.json` | 默认配置 |
| `backend/CesiumAI.Api.Tests/Services/AgentFactoryTests.cs` | 相对路径解析测试对齐新默认布局 |
| `README.md` | 安装 / 配置 / 发布说明 |
| `Docs/prd.md` | F3 → F1 决策更新 |
| `Docs/superpowers/plans/2026-07-16-cesiumai-mvp.md` | 顶部标注已 superseded（可选短注） |

---

### Task 1: 添加 submodule 并清理 ignore

**Files:**
- Create: `.gitmodules`
- Create: `backend/astrox-skills/`（submodule）
- Modify: `.gitignore`（删除 `backend/skills/`）

**Interfaces:**
- Consumes: 无
- Produces: checkout 后存在 `backend/astrox-skills/skills/` 目录树

- [ ] **Step 1: 若本地仍有手动 `backend/skills/`，先移走以免混淆**

```powershell
if (Test-Path backend/skills) {
  Rename-Item backend/skills backend/skills.manual-backup
}
```

- [ ] **Step 2: 添加 submodule**

```powershell
git submodule add https://gitee.com/blitheli/astrox-skills.git backend/astrox-skills
git submodule status
```

Expected: `.gitmodules` 出现；`backend/astrox-skills` 显示已 checkout 的 commit；存在 `backend/astrox-skills/skills`。

- [ ] **Step 3: 从 `.gitignore` 删除 `backend/skills/` 行**

`.gitignore` 最终应类似：

```gitignore
**/bin/
**/obj/
frontend/node_modules/
frontend/dist/
frontend/playwright-report/
frontend/test-results/
*.user
appsettings.Development.json
```

- [ ] **Step 4: 验证 submodule 内容可用**

```powershell
Test-Path backend/astrox-skills/skills
Get-ChildItem backend/astrox-skills/skills | Select-Object -First 5 Name
```

Expected: `True`，且列出若干 skill 目录。

---

### Task 2: 更新默认 Skills 路径与测试

**Files:**
- Modify: `backend/CesiumAI.Api/Configuration/SkillsOptions.cs`
- Modify: `backend/CesiumAI.Api/appsettings.json`
- Modify: `backend/CesiumAI.Api.Tests/Services/AgentFactoryTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `backend/astrox-skills/skills` 布局约定
- Produces: 默认 `Skills:Path = "../astrox-skills/skills"`

- [ ] **Step 1: 先改测试，使“相对 content root 解析”用例反映新布局**

在 `AgentFactoryTests.CreateAsync_ResolvesSkillsRelativeToContentRoot_WithoutCallingRemoteServices` 中，把临时目录布局从 `skills` 改为 `astrox-skills/skills`，路径改为 `../astrox-skills/skills`：

```csharp
[Fact]
public async Task CreateAsync_ResolvesSkillsRelativeToContentRoot_WithoutCallingRemoteServices()
{
    string parent = Directory.CreateTempSubdirectory().FullName;
    string contentRoot = Directory.CreateDirectory(Path.Combine(parent, "api")).FullName;
    Directory.CreateDirectory(Path.Combine(parent, "astrox-skills", "skills"));

    try
    {
        AgentFactory factory = CreateFactory(contentRoot, "../astrox-skills/skills");

        AgentRuntime runtime = await factory.CreateAsync("session", CancellationToken.None);

        runtime.Should().NotBeNull();
    }
    finally
    {
        Directory.Delete(parent, recursive: true);
    }
}
```

- [ ] **Step 2: 运行该测试，确认仍可通过（路径仅由测试入参决定；在改默认值之前应仍 PASS）**

```powershell
dotnet test backend/CesiumAI.Api.Tests/CesiumAI.Api.Tests.csproj --filter "FullyQualifiedName~CreateAsync_ResolvesSkillsRelativeToContentRoot"
```

Expected: PASS（此步验证测试本身正确）。

- [ ] **Step 3: 更新默认配置**

`SkillsOptions.cs`：

```csharp
public string Path { get; init; } = "../astrox-skills/skills";
```

`appsettings.json`：

```json
"Skills": {
  "Path": "../astrox-skills/skills"
}
```

- [ ] **Step 4: 运行后端测试套件**

```powershell
dotnet test backend/CesiumAI.Api.Tests/CesiumAI.Api.Tests.csproj
```

Expected: 全部 PASS。

- [ ] **Step 5: 用真实 content root 烟测默认路径解析**

```powershell
dotnet exec --project 不需要
# 用一小段 PowerShell 验证 ResolveExistingDirectory：
dotnet run --project backend/CesiumAI.Api --no-build 2>&1 | Select-Object -First 1
```

更稳妥：

```powershell
$root = Resolve-Path backend/CesiumAI.Api
$skills = Resolve-Path (Join-Path $root "..\astrox-skills\skills")
Test-Path $skills
```

Expected: `True`。若 API 正在运行且 User Secrets 仍指向旧 `../skills`，需提示用户更新或删除该 secret。

---

### Task 3: 更新 README / PRD / 历史计划标注

**Files:**
- Modify: `README.md`
- Modify: `Docs/prd.md`
- Modify: `Docs/superpowers/plans/2026-07-16-cesiumai-mvp.md`（顶部短注即可）

**Interfaces:**
- Consumes: Task 1–2 的路径与流程
- Produces: 文档与实现一致

- [ ] **Step 1: 替换 README「安装 astrox-skills（F3）」整节**

改为：

```markdown
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
```

- [ ] **Step 2: 更新 README 配置说明中的默认路径**

- 将「`backend/skills`」改为「`backend/astrox-skills/skills`」
- 将示例 `Skills:Path` / `Skills__Path` 默认值改为 `../astrox-skills/skills`
- 发布段改为：

```bash
mkdir -p publish/skills
cp -R backend/astrox-skills/skills/. publish/skills/
```

运行时仍可用 `Skills__Path=../skills`（相对 `publish/api`）。

- [ ] **Step 3: 更新「不要提交」清单**

删除「不要提交 `backend/skills/`」；改为提醒 submodule 内容由 submodule commit 管理，不要把密钥提交进仓。

- [ ] **Step 4: 更新 `Docs/prd.md`**

1. 状态行决策标记：将 F3 改为 F1（或并列注明已升级）。
2. 决策表：

```markdown
| F1 | Skills：`backend/astrox-skills` Git submodule；加载 `skills/` |
```

删除或改写原 F3 行。

3. §7.4：

```markdown
- **接入（F1）**：以 Git submodule 置于 `backend/astrox-skills`；`AgentSkillsProvider` 加载其 `skills/` 目录（默认 `Skills:Path=../astrox-skills/skills`）
```

4. §8 非目标：删除「Git submodule 管理 skills（F1，后续可选）」
5. §11：删除「Skills 引用升级：F3 → F1」或改为「已完成」

- [ ] **Step 5: 在旧 MVP plan 顶部加短注**

在 `Docs/superpowers/plans/2026-07-16-cesiumai-mvp.md` 文首加：

```markdown
> **Note (2026-07-16):** Skills 接入已从 F3 升级为 F1 submodule。见 `Docs/superpowers/specs/2026-07-16-astrox-skills-submodule-design.md` 与 `Docs/superpowers/plans/2026-07-16-astrox-skills-submodule.md`。下文中 `backend/skills` 手动复制步骤已过时。
```

- [ ] **Step 6: 文档抽查**

```powershell
rg -n "backend/skills|F3|手动.*clone|手动.*复制" README.md Docs/prd.md Docs/superpowers/specs/2026-07-16-astrox-skills-submodule-design.md Docs/superpowers/plans/2026-07-16-astrox-skills-submodule.md
```

Expected: README / prd 现行说明不再把 F3 当作当前方案；历史 plan 仅保留 superseded 注释中的旧路径提及可接受。

---

### Task 4: 端到端验收

**Files:** 无新增代码

- [ ] **Step 1: 确认 git 状态包含预期文件**

```powershell
git status --short
git submodule status
```

Expected：`.gitmodules`、`backend/astrox-skills`（gitlink）、配置/测试/文档改动；无密钥文件。

- [ ] **Step 2: 全量后端测试**

```powershell
dotnet test backend/CesiumAI.Api.Tests/CesiumAI.Api.Tests.csproj
```

Expected: PASS。

- [ ] **Step 3: 默认路径存在性**

```powershell
Test-Path backend/astrox-skills/skills
```

Expected: `True`。

- [ ] **Step 4:（仅当用户明确要求）提交**

建议 commit message：

```text
chore: manage astrox-skills via git submodule

Replace manual F3 copy of backend/skills with a pinned submodule at
backend/astrox-skills so clones and deploys can init skills reproducibly.
```

不要 push，除非用户要求。

---

## Self-Review Checklist

1. Spec coverage: submodule 路径、默认 Path、gitignore、README、prd、publish 复制、测试、不自动 commit — 均有对应 Task。
2. Placeholder scan: 无 TBD / “类似 Task N”。
3. Type consistency: `Skills:Path` 字符串始终为 `../astrox-skills/skills`；publish 运行时仍为 `../skills`。
