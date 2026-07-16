# CesiumAI — 产品需求 / 技术规格（MVP）

> 状态：已评审定稿（对话决策 A / A2 / B1 / C2 / D2 / E1 / 方案1 / F1）  
> 日期：2026-07-16  
> 关联：`Docs/需求初步描述.md`、`https://gitee.com/blitheli/astrox-skills.git`

---

## 1. 背景与目标

构建以 **Cesium.js** 为基础的航天任务智能设计与可视化网站：用户通过自然语言与 Agent 对话，Agent 调用航天算法 Skills，产出 **CZML 场景操作**；前端将操作应用到三维地球，完成地面站与卫星轨迹等可视化。

参考 Cesium Sandcastle Copilot 的「Viewer + 侧栏对话」布局，但**不做代码沙盒**：不生成/执行任意前端 JS，而以 CZML 为场景载体。

### 1.1 成功标准（MVP）

用户可在产品页完成：

1. 清空当前场景
2. 修改已有地面站（如 sanya）经纬高
3. 添加地面站（给定经纬高）
4. 添加 900km SSO 卫星，经 J2/递推得到一天星历并以 CZML positions 动态显示

---

## 2. 用户与典型场景

**用户**：航天任务分析/设计人员（单机 Web 使用）。

| 场景 | 用户输入示例 | 系统行为 |
|---|---|---|
| 清空 | 清空当前场景 | `sceneOps: [{ op: "clear" }]` |
| 改站 | 把 sanya 改到 lon,lat,alt | `UpsertFacility` → upsert packet |
| 加站 | 添加地面站 -100, 30.2, 10 | 同上 |
| 加星 | 添加 900km SSO，J2 递推一天 | `AddSatelliteJ2` → Astrox/Skills → upsert 卫星 |

---

## 3. 决策记录

| ID | 决策 |
|---|---|
| A | 前端持有完整 CZML，为场景权威 |
| A2 | 请求附带 `sceneSummary`；命中实体时附带 `relevantPackets` |
| B1 | 响应结构化 `{ message, sceneOps }` |
| C2 | CZML / `sceneOps` 由 C# Tool 组装，LLM 不手写星历 CZML |
| D2 | MVP 范围：清空 + 地面站增改 + SSO/J2 卫星 |
| E1 | 同步 `POST /api/chat` |
| Arch-1 | 薄 ASP.NET API + 每轮 SceneOp 收集器 |
| Skills | 源仓库 `https://gitee.com/blitheli/astrox-skills.git` |
| F1 | Skills：`backend/astrox-skills` Git submodule；加载 `skills/` |

---

## 4. 系统架构

### 4.1 逻辑架构

```text
┌─────────────────────────────────────────────────────────────┐
│  React (Vite) SPA                                            │
│  ChatPanel ──▶ SceneManager (CZML 权威) ──▶ CzmlDataSource   │
│                         └──────────────▶ Cesium.Viewer       │
└───────────────────────────┬─────────────────────────────────┘
                            │ POST /api/chat
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  ASP.NET Core                                                │
│  ChatController → ChatService                                │
│       ├─ AIAgent (Microsoft Agent Framework)                 │
│       ├─ AgentSkillsProvider (backend/astrox-skills/skills)   │
│       ├─ HttpGet/HttpPost → Astrox WebAPI                    │
│       └─ 场景 Tools → ISceneOpCollector → sceneOps           │
└─────────────────────────────────────────────────────────────┘
```

- **Frontend**：React (Vite) + Cesium Viewer + ChatPanel + SceneManager
- **Backend**：ASP.NET Core + Microsoft Agent Framework（`AIAgent`）+ AgentSkillsProvider + Astrox HTTP
- **External**：Astrox WebAPI、LLM（OpenAI 兼容，如 Kimi）、astrox-skills 仓库

### 4.2 数据流

用户消息 → 前端组装 summary / packets → `POST /api/chat` → Agent 调 Tools → Tools 写入 SceneOpCollector → 返回 `{ message, sceneOps }` → SceneManager `load` / `process` → Viewer 更新。

### 4.3 仓库结构（monorepo）

```text
CesiumAI/
├── Docs/
│   └── prd.md
├── frontend/                  # React + Vite + Cesium
│   └── src/
│       ├── components/        # ChatPanel, ViewerHost
│       ├── scene/             # SceneManager, czml helpers
│       ├── api/               # chat client
│       └── app/
└── backend/
    ├── CesiumAI.Api/
    │   ├── Controllers/
    │   ├── Services/          # ChatService, SceneOpCollector
    │   ├── Tools/             # ClearScene, UpsertFacility, AddSatelliteJ2...
    │   ├── Models/
    │   └── Program.cs
    └── astrox-skills/           # Git submodule（F1）
```

### 4.4 职责边界

| 组件 | 负责 | 不负责 |
|---|---|---|
| 前端 SceneManager | CZML 权威、摘要提取、apply ops、渲染 | 轨道计算 |
| ChatPanel | 展示对话、发请求、把 sceneOps 交给 SceneManager | 解析/生成 CZML |
| ChatService | 会话、组 prompt、跑 Agent、打包响应 | 持有完整场景 |
| 场景 Tools | 调算法并产出合法 `SceneOp` | 直接改前端 Viewer |
| 泛型 HttpGet/Post | 探索 Skills / 临时查询 | 作为场景写操作的主路径 |

---

## 5. API 契约

### 5.1 端点

`POST /api/chat`

- Content-Type: `application/json`
- 同步等待 Agent + Tools 完成后返回（E1）
- 失败时 HTTP 4xx/5xx + `{ "error": "...", "detail": "..." }`（可选）

会话：用 `sessionId`。首轮可不传或传空，服务端创建并在响应里回传；之后前端原样带回。

### 5.2 请求 `ChatRequest`

```ts
type ChatRequest = {
  message: string;
  sessionId?: string | null;
  sceneSummary: SceneSummary;
  relevantPackets?: object[];
};

type SceneSummary = {
  documentClock?: {
    interval?: string;
    currentTime?: string;
  };
  entities: EntitySummary[];
};

type EntitySummary = {
  id: string;
  name?: string;
  type: "facility" | "satellite" | "other";
  lon?: number;
  lat?: number;
  alt?: number;
  orbitHint?: string;
};
```

**前端如何填 `relevantPackets`（MVP 规则，A2）**

1. 用户消息里出现实体 id/名称 → 带上对应完整 packet
2. 当前 Viewer 有选中实体 → 带上选中项
3. 否则只发 `sceneSummary`，`relevantPackets` 可省略或 `[]`

### 5.3 响应 `ChatResponse`

```ts
type ChatResponse = {
  sessionId: string;
  message: string;
  sceneOps: SceneOp[];
};

type SceneOp =
  | { op: "clear" }
  | { op: "upsert"; packets: object[] }
  | { op: "delete"; ids: string[] };
```

约定：

- 一轮里可有多个 `sceneOps`，前端**按数组顺序**执行
- `upsert.packets` 里每个对象必须有 CZML `id`
- 卫星星历放在 packet 的 `position`（与 Astrox / skills 输出对齐，如 `cartesian` / `cartesianVelocity`）
- `message` 与 `sceneOps` 解耦：可以只有文字没有 ops（纯问答）

### 5.4 SceneOp 语义

| op | 前端行为 |
|---|---|
| `clear` | `CzmlDataSource.load(emptyDocument)`，重置内存 document |
| `upsert` | `process(packets)`，合并进 document |
| `delete` | `removeById` + 从 document 删除 |

---

## 6. 前端规格

### 6.1 UI

```text
┌────────────────────────────────────────┬──────────────────┐
│  Cesium.Viewer（主视图，长期存活）        │  ChatPanel       │
│                                        │  - 消息列表       │
│                                        │  - 输入框         │
│                                        │  - 发送 / loading │
└────────────────────────────────────────┴──────────────────┘
```

- 无代码编辑器、无 iframe Bucket、无 “Run 重建页面”
- Viewer 只初始化一次；场景变化只动 `CzmlDataSource`

### 6.2 SceneManager

作为前端的**场景权威**（建议 `src/scene/SceneManager.ts`）：

| 能力 | 说明 |
|---|---|
| 持有 `sceneDocument` | 完整 CZML 数组（含 `document` packet） |
| 持有 `CzmlDataSource` | 单一实例，`viewer.dataSources.add` 一次 |
| `buildSummary()` | 从 document 抽 `EntitySummary[]` |
| `pickRelevantPackets(ids)` | 按 id 取出完整 entity packet（供 A2） |
| `applySceneOps(ops)` | 按序执行 clear / upsert / delete，并同步 `sceneDocument` |
| `getSelectedEntityIds()` | 供 Chat 组装 `relevantPackets` |

原则：**先更新内存 document，再驱动 DataSource**（或两者在同一函数内保持一致）。

### 6.3 与 Cesium API 的映射

- 增量：`CzmlDataSource.process`
- 全量替换 / 清空：`CzmlDataSource.load`
- 主路径不使用零散 `viewer.entities.add`

空场景最小 document 示例：

```json
[
  {
    "id": "document",
    "name": "CesiumAI Scene",
    "version": "1.0",
    "clock": {
      "interval": "2026-01-01T00:00:00Z/2026-01-02T00:00:00Z",
      "currentTime": "2026-01-01T00:00:00Z",
      "multiplier": 60
    }
  }
]
```

### 6.4 模块划分建议

```text
frontend/src/
  components/
    ViewerHost.tsx
    ChatPanel.tsx
  scene/
    SceneManager.ts
    emptyDocument.ts
    summary.ts
  api/
    chat.ts
  app/App.tsx
```

状态管理：MVP 用 React context 或模块单例持有 `SceneManager` 即可。

### 6.5 非目标（前端）

- 不在前端执行 Agent 生成的任意 JS
- 不用 `viewer.entities.add` 作为主路径
- 不做多人协作场景同步

---

## 7. 后端规格

### 7.1 ChatService 主流程

```text
收到 ChatRequest
  → 解析/创建 sessionId，取 AgentSession
  → 绑定本轮 ISceneOpCollector（每请求闭包 Tools，推荐）
  → 将 message + sceneSummary + relevantPackets 拼成 User 消息
  → await agent.RunAsync(userMessage, session)
  → message = Run 结果文本
  → sceneOps = collector.Drain()
  → 返回 ChatResponse
```

要点：

- Agent 文本进 `message`；**场景变更只认 collector**，不从模型输出里正则抠 CZML
- 现有 `AgentSkillsProvider` + `HttpGet` / `HttpPost` 保留，供查 TLE、读 skill 文档等

### 7.2 Agent 组装

基于现有 demo（`Docs/c#-demo.cs` / 原 `c#-demo.cs`）：

```text
OpenAIClient(Kimi/Moonshot endpoint)
  .GetChatClient(model)
  .AsAIAgent(
     Name = "SpaceAgent"
     Instructions = 航天任务可视化助手 + Astrox BASE_URL
                    + 「改场景必须用场景 Tools」
     Tools = [
       GetPeriod, HttpGet, HttpPost,
       ClearScene, UpsertFacility,
       AddSatelliteJ2, DeleteEntity
     ]
     AIContextProviders = [ AgentSkillsProvider(skills/) ]
  )
```

配置：

- `ApiKey` / Endpoint / Model → UserSecrets 或环境变量
- `Astrox:BaseUrl` → 可配置（demo 默认 `http://astrox.cn:8765`）
- CORS：允许 frontend 开发源（如 `http://localhost:5173`）

### 7.3 MVP 场景 Tools

| Tool | 入参（示意） | 内部行为 | 写入 SceneOp |
|---|---|---|---|
| `ClearScene` | 无 | 无外部调用 | `{ op: "clear" }` |
| `UpsertFacility` | `id, name?, lon, lat, alt` | 本地拼 CZML point packet | `{ op: "upsert", packets: [...] }` |
| `DeleteEntity` | `ids: string[]` | 校验非空 | `{ op: "delete", ids }` |
| `AddSatelliteJ2` | `id, name?, altitudeKm=900, hours=24, ...` | SSO/轨道 skill → Propagator/J2 → 转 CZML | `{ op: "upsert", packets: [satellite] }` |

`AddSatelliteJ2`：

- 优先复用 astrox-skills 指引的 Astrox 路径
- 优先复用仓库内 `convert-czmlPosition` / `CzmlPositionOut` 等约定做归一化
- Tool **负责把 API 结果变成合法 CZML**，再写入 collector
- 失败时不写入残缺 packet

### 7.4 依赖：astrox-skills

- **源仓库**：`https://gitee.com/blitheli/astrox-skills.git`
- **内容**：航天动力学算法 SKILLS；`skills/<skill-name>/SKILL.md` + `fixtures/`；公共协议在 `skills/shared-docs/`（含 CZML position 等 schema）；`astrox-web-api.json` 可作为 API 参考
- **接入（F1）**：以 Git submodule 置于 `backend/astrox-skills`；`AgentSkillsProvider` 加载其 `skills/` 目录（默认 `Skills:Path=../astrox-skills/skills`）
- **职责划分**：Skill + 泛型 HTTP 用于计算/查询；**写场景**只走强类型场景 Tools

### 7.5 Session 存储

| 项 | MVP 策略 |
|---|---|
| `AgentSession` | 进程内 `ConcurrentDictionary<sessionId, AgentSession>` |
| 过期 | 滑动过期或不清理（开发期） |
| 持久化 | 不做（重启丢失对话记忆；场景本就在前端） |
| `sessionId` | GUID 字符串，响应回传 |

场景**不**进 Session；Session 只服务多轮对话与 tool 上下文。

### 7.6 Instructions 约束（摘要）

1. 你是航天任务可视化助手；场景变更只能通过 ClearScene / UpsertFacility / DeleteEntity / AddSatelliteJ2
2. 禁止在回复中输出 CZML/JSON 场景块当作执行手段
3. 需要轨道/TLE/递推时先 `load_skill` 再 Http 调 Astrox
4. 用户未要求改场景时，只回答问题，不调用场景 Tools
5. 用简洁中文总结工具结果

### 7.7 错误与超时

- Astrox 超时/非 2xx：Tool 返回可读错误；Agent 写入 `message`；无对应成功 `sceneOps`
- 整请求超时（建议 120s）：API 返回 504/408
- 前端 loading 覆盖等待期；不自动重试改场景（避免重复 upsert）

---

## 8. MVP 非目标

- Sandcastle 式代码沙盒 / Diff Apply
- 后端持有完整场景、多人协同
- SSE / WebSocket 流式
- 登录鉴权、场景落库、撤销栈
- Access / 光照等更多分析 Skills 的产品化封装（可随后加 Tool）

---

## 9. 实现顺序

1. 前端 Viewer + SceneManager + 本地样例 CZML
2. 后端假 chat（固定 `sceneOps`）打通前端 apply
3. 接入 Agent：`ClearScene` / `UpsertFacility` / `DeleteEntity`
4. 接入 `AddSatelliteJ2`（astrox-skills + Astrox）
5. 补 A2：`sceneSummary` + `relevantPackets` 组装与四场景验收

---

## 10. 风险与对策

| 风险 | 影响 | 对策 |
|---|---|---|
| LLM 不调 Tool、改口胡写 CZML | 场景不更新 | Instructions 强约束 + 只信 collector |
| Astrox/递推慢或失败 | E1 长时间转圈 | 超时与友好错误；后续 E2 推进度 |
| CZML `position` 格式与 Cesium 不一致 | 轨迹不显示 | Tool 内固定模板 + golden CZML 样例；对齐 skills schema |
| collector 串请求 | ops 错乱 | MVP 每请求闭包 Tools |
| 星历过大 | JSON 响应膨胀 | MVP 限制一天、步长可配 |
| Skills 与 Tool 职责重叠 | Agent 乱走泛型 HttpPost 拼场景 | Instructions：场景写操作只用场景 Tools |

---

## 11. 后续扩展（不阻塞 MVP）

1. **E2 流式**：先推 assistant token，最后推 `scene_ops` 事件
2. **场景持久化**：`sceneDocument` 存 localStorage / 后端按 projectId
3. **更多 Tools**：可见性、地面站网、星座批量生成
4. **sceneOps 校验器**：JSON Schema 校验 packet 再返回前端
5. **sceneOps 校验器**：JSON Schema 校验 packet 再返回前端
6. **选中实体 → 自动进 relevantPackets**（增强 A2）

---

## 12. 验收清单

- [ ] 「清空当前场景」→ 无业务实体，document 回到空 document
- [ ] 「修改 sanya 经纬高」→ 同 id upsert，点位置与 summary 更新
- [ ] 「添加地面站 -100, 30.2, 10」→ 新 point 可见且进 document
- [ ] 「添加 900km SSO + J2 一天」→ 卫星动态轨迹可播
- [ ] 纯问答不产生 `sceneOps`，场景不变
- [ ] 不存在「聊天里贴 CZML、前端正则执行」路径
