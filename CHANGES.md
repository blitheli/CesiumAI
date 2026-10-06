# 更新记录

按日期倒序记录每次更新。每个日期使用二级标题 `## YYYY-MM-DD`，同日多项改动写在同一标题下。
前端构建时会读取本文件中最新的日期标题，显示在页面副标题末尾（如 `用自然语言探索和编辑场景-20261006`）。

## 2026-10-06

- 前端副标题末尾显示最新更新日期，构建时由 `frontend/vite.config.ts` 从本文件最新日期标题读取并通过 Vite `define` 注入（`__APP_LAST_UPDATED__`），无需两处手工维护；开发时修改本文件会自动重启 dev server。
- 新增本文件 `CHANGES.md` 记录每次更新，并在 `AGENTS.md` 中约定每次更新须追加条目；前端部署工作流在本文件变更时也会触发，确保线上日期同步。
- 修复：前端部署构建注入 `VITE_CESIUM_ION_TOKEN`，README 补充对应 GitHub Secret 说明（[PR #6](https://github.com/blitheli/cesiumai/pull/6)）。
- 后端 Agent 迁移到 `Microsoft.Agents.AI.Harness` 1.23.0，并新增 SSE 流式聊天，前端聊天面板支持流式显示助手回复（[PR #5](https://github.com/blitheli/cesiumai/pull/5)）。
