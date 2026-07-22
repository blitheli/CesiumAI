# 阿里云 GitHub Actions 部署 + 内部 skills 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans（本会话按用户要求直接 inline 执行）。Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将运行时 skills 固定为 API content root 内的 `skills/`，并新增前后端独立的 GitHub Actions，经 OpenSSH 部署到阿里云 IIS。

**Architecture:** submodule 仍位于 `backend/astrox-skills`；构建时复制到 `CesiumAI.Api/skills`（content root）与 publish 输出；默认 `Skills:Path=skills`。两个 workflow 分别构建前端 dist / `dotnet publish`，SCP 到 IIS 目录；后端部署前后停启应用池 `CesiumAI.backend`。

**Tech Stack:** .NET 10 MSBuild、GitHub Actions、`appleboy/scp-action`、`appleboy/ssh-action`、OpenSSH、IIS。

**Spec:** `Docs/superpowers/specs/2026-07-22-aliyun-github-actions-deploy-design.md`

---

### Task 1: Skills 内部路径与构建复制

**Files:**
- Modify: `backend/CesiumAI.Api/CesiumAI.Api.csproj`
- Modify: `backend/CesiumAI.Api/Configuration/SkillsOptions.cs`
- Modify: `backend/CesiumAI.Api/appsettings.json`
- Modify: `.gitignore`
- Modify: `backend/CesiumAI.Api.Tests/Services/AgentFactoryTests.cs`

- [x] 默认 `Path` 改为 `skills`
- [x] csproj：`BeforeBuild` 将 `..\astrox-skills\skills\**\*` 复制到 `$(MSBuildProjectDirectory)\skills\`；`Publish` 后确保 publish 目录含 `skills\`
- [x] `.gitignore` 忽略 `backend/CesiumAI.Api/skills/`
- [x] 测试改为 content root 内 `skills` 布局
- [x] `dotnet test` 相关用例通过

### Task 2: 文档

**Files:**
- Modify: `README.md`
- Modify: `AGENTS.md`
- Modify: `Docs/prd.md`（skills 路径相关句）

- [x] 说明运行时为内部 `skills/`；submodule 仍为来源
- [x] 生产部署改为 publish 目录内已含 skills；去掉并列 `../skills`
- [x] 补充 GitHub Actions / Secrets / 应用池说明

### Task 3: Workflows

**Files:**
- Create: `.github/workflows/deploy-frontend.yml`
- Create: `.github/workflows/deploy-backend.yml`

- [x] 前端：Node 22、`npm ci`/`build`、SCP 到 `D:/IIS/ASTROX.CesiumAI.frontend`
- [x] 后端：submodules、`dotnet publish`、停池、SCP、启池

### Task 4: 验证

- [x] `dotnet test`（Skills/ChatController/AgentFactory 相关 19 项通过）
- [x] `dotnet publish` 后确认 `publish/api/skills` 存在
