# 更新记录

按日期倒序记录每次更新。每个日期使用二级标题 `## YYYY-MM-DD`，同日多项改动写在同一标题下。
前端构建时会读取本文件中最新的日期标题，显示在页面副标题末尾（如 `用自然语言探索和编辑场景-20261006`）。

## 2026-10-07

- Cursor Cloud 环境：`install` 脚本改为幂等安装 .NET 10 SDK（此前快照中实际缺失）、Node 22、skills submodule、npm 依赖与 Playwright Chromium；`.gitmodules` 中 skills submodule 上游改为 `https://github.com/blitheli/astrox-skills.git`；同步修正 `AGENTS.md` 中的环境说明（[PR #11](https://github.com/blitheli/CesiumAI/pull/11)）。

## 2026-10-06

- 前端聊天面板助手回复按 GFM 渲染 Markdown（表格、加粗、行内代码、列表、代码块、链接），使用 `react-markdown` + `remark-gfm`；不渲染原始 HTML（作为纯文本显示），链接新窗口打开并带 `rel="noopener noreferrer"`；表格在窄面板内横向滚动，兼容流式输出的半截内容与闪烁光标；用户消息保持纯文本（[PR #8](https://github.com/blitheli/CesiumAI/pull/8)）。
- 修复：同一会话在出现工具调用后，后续轮次全部失败（界面显示「Agent request failed.」）。根因是 kimi 流式回复首个分片为空串，被推理内容隔开后会话历史中残留空文本片段，Moonshot 以 400 `text content is empty` 拒绝；另外工具循环中途失败会遗留未应答的 tool_calls，同样使该会话此后每轮都返回 400。新增 `ChatHistorySanitizingChatClient`，发往 LLM 前清洗历史；前端 `web.config` 对 `/api` 关闭动态压缩，README 补充 ARR 响应缓冲与超时设置（[PR #9](https://github.com/blitheli/CesiumAI/pull/9)）。
- 前端副标题末尾显示最新更新日期，构建时由 `frontend/vite.config.ts` 从本文件最新日期标题读取并通过 Vite `define` 注入（`__APP_LAST_UPDATED__`），无需两处手工维护；开发时修改本文件会自动重启 dev server。
- 新增本文件 `CHANGES.md` 记录每次更新，并在 `AGENTS.md` 中约定每次更新须追加条目；前端部署工作流在本文件变更时也会触发，确保线上日期同步。
- 修复：前端部署构建注入 `VITE_CESIUM_ION_TOKEN`，README 补充对应 GitHub Secret 说明（[PR #6](https://github.com/blitheli/cesiumai/pull/6)）。
- 后端 Agent 迁移到 `Microsoft.Agents.AI.Harness` 1.23.0，并新增 SSE 流式聊天，前端聊天面板支持流式显示助手回复（[PR #5](https://github.com/blitheli/cesiumai/pull/5)）。
