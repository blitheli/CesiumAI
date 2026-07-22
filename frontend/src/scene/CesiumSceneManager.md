# CesiumSceneManager — 结构与功能说明

前端场景中枢：维护**内存 CZML 文档**与球上 **`CzmlDataSource`** 的一致，并把后端下发的 `sceneOps` 串行落到 Viewer。

相关源码：`CesiumSceneManager.ts`  
配套模块：`sceneDocument.ts`（纯文档归约）、`sceneStyle.ts`（视觉补丁）、`summary.ts`（摘要）、`CesiumCameraController.ts`（相机）  
总览：`Docs/前端说明.md` §4.6

---

## 1. 在系统中的位置

```text
用户发消息
    │
    ▼
App.handleSend
    ├─ buildSummary()              → ChatRequest.sceneSummary
    ├─ inferRelevantEntityIds(...) → 相关 id
    ├─ pickRelevantPackets(ids)    → ChatRequest.relevantPackets
    ├─ postChat(...)
    └─ applySceneOps(response.sceneOps)  ← 本类核心入口
            │
            ├─ reduceSceneDocument / applyStylePatch  （改内存文档）
            ├─ CzmlDataSourcePort.load / process       （改球上实体）
            └─ CameraController.apply                  （改相机，不改文档）
```

产品路径是 **聊天 → sceneOps → SceneManager**；CZML 权威在前端内存文档，后端只下发意图。

---

## 2. 结构概览

### 2.1 内部状态

| 成员 | 作用 |
|------|------|
| `sceneDocument` | 内存权威 CZML：`document` + 业务实体 packet 数组 |
| `emptyDocument` | 空场景模板；`clear` 时归约到此 |
| `dataSourcePort` | 对 `CzmlDataSource` + Viewer 时钟的可测试抽象 |
| `cameraController` | 解释 `op: "camera"` |
| `selectedEntityIds` | 与 Viewer 选中同步，供相关实体推断 |
| `operationQueue` | Promise 链：保证多次 `applySceneOps` 串行 |
| `initialized` / `initialization` | `initialized`：是否已完成初始化；`initialization`：进行中的初始化 Promise。重复调用 `initialize` 直接返回；并发调用共用同一次初始化 |

### 2.2 主要类型

| 类型 | 含义 |
|------|------|
| `CzmlDataSourcePort` | `load` / `process` / `removeById` / 时钟同步与诊断 |
| `CesiumCzmlDataSourcePort` | 真实 Cesium 实现（内部类） |
| `CzmlDocumentClock` | document.clock：`interval` + `currentTime` + 可选 `multiplier` |
| `ViewerClockSnapshot` | upsert/style 失败回滚用的 Viewer 时钟快照 |
| `SceneDiagnostics` | 只读诊断：时钟、相机、实体可视化状态 |

### 2.3 依赖协作

| 依赖 | 职责边界 |
|------|----------|
| `reduceSceneDocument` | 只改 packet 数组，不碰 Cesium；`camera` 在此层会抛错 |
| `applyStylePatch` | style 白名单校验 + 视觉字段深合并 |
| `buildSceneSummary` / `pickRelevantPackets` | 摘要与相关 packet（本类做薄封装） |
| `CesiumCameraController` | focus / track / orbit 等 |

---

## 3. 公开 API

### 3.1 构造

```ts
new CesiumSceneManager(
  emptyDocumentFactory?,  // 默认 createEmptyDocument(new Date())
  dataSourcePort?,         // 单测可注入假端口
  cameraController?,       // 单测可注入假相机
)
```

生产环境一般只 `new CesiumSceneManager()`，由 `ViewerHost` 在创建 Viewer 后调用 `initialize(viewer)`。

### 3.2 生命周期

| 方法 | 说明 |
|------|------|
| `initialize(viewer?)` | 挂 DataSource、创建相机控制器、`load` 空文档。幂等。未注入 port 时必须传 `viewer`。 |
| `destroy()` | 释放相机控制器；Viewer 销毁由 `ViewerHost` 负责。 |

### 3.3 场景变更

| 方法 | 说明 |
|------|------|
| `applySceneOps(ops)` | 深拷贝后入队，按序应用 clear / upsert / delete / style / camera。 |

### 3.4 聊天请求辅助

| 方法 | 说明 |
|------|------|
| `buildSummary()` | 内存文档 → 轻量 `SceneSummary` |
| `pickRelevantPackets(ids)` | 按 id 深拷贝完整 packet |
| `setSelectedEntityIds` / `getSelectedEntityIds` | 选中同步 |

### 3.5 诊断

| 方法 | 说明 |
|------|------|
| `getSceneDiagnostics()` | Viewer/DataSource + 相机 + canonical position 元数据（测试/调试） |

---

## 4. `applySceneOps` 行为详解

### 4.1 串行队列

```ts
await manager.applySceneOps(opsA); // 不必等完也可再调
void manager.applySceneOps(opsB);  // B 会排在 A 之后
```

- 入队前 `structuredClone(ops)`，避免外部事后改数组影响队列快照。
- 某次失败：该次 Promise **reject**；队列仍继续（失败被 `catch` 掉以便后续入队）。

### 4.2 各 op 落地方式

| `op` | 内存文档 | 球上 DataSource | 其它 |
|------|----------|-----------------|------|
| `clear` | 归约为 `emptyDocument` | `load` 全量 | 成功后再 `onSceneCleared` |
| `upsert` | `reduceSceneDocument` + 可选按 `availability` 对齐 document.clock | `removeById` + `process`；interval 变化则 `syncViewerClock` | 失败则 `load` 回滚 + 恢复时钟/跟踪 |
| `delete` | 逐 id 归约 | `removeById` | 先通知相机 `onEntitiesDeleted` |
| `style` | `applyStylePatch` 得到完整 packet | 替换该实体 | 失败同样回滚 |
| `camera` | **不改** | 不改 | 交给 `CameraController.apply` |

要点：

- 改 `position` / `availability` → 只能 **upsert 整包**，不能靠 style。
- `document` 由前端维护；业务 upsert `id: "document"` 在归约层被忽略。
- upsert 同一批里同 id 多次出现时，只保留**最后一次**（`lastPacketPerId`）。

### 4.3 upsert / style 失败回滚

失败时尽量恢复「球上 = 先前内存文档」：

1. `load(previousDocument)`
2. `restoreViewerClock(snapshot)`
3. `rebindAfterReload(trackedSnapshot)`

`load` 也失败则抛 `AggregateError`。避免内存文档与球长期不一致。

---

## 5. 使用方法

### 5.1 应用内标准路径（推荐）

`main.tsx` 创建单例，`ViewerHost` 初始化，`App` 发聊天后应用 ops：

```ts
// main.tsx
const sceneManager = new CesiumSceneManager();

// ViewerHost：Viewer 就绪后
await sceneManager.initialize(viewer);

// App：发送消息
const summary = sceneManager.buildSummary();
const relevantIds = inferRelevantEntityIds(
  text,
  summary,
  sceneManager.getSelectedEntityIds(),
);
const relevantPackets = sceneManager.pickRelevantPackets(relevantIds);

const response = await postChat({
  message: text,
  sessionId,
  sceneSummary: summary,
  relevantPackets,
});

await sceneManager.applySceneOps(response.sceneOps);
```

卸载时：`sceneManager.destroy()`（与 Viewer.destroy 一并调用）。

### 5.2 单测注入（无真实 Viewer）

```ts
const port: CzmlDataSourcePort = {
  load: async () => {},
  process: async () => {},
  removeById: () => true,
  syncViewerClock: () => {},
  snapshotViewerClock: () => ({ /* ... */ }),
  restoreViewerClock: () => {},
  getSceneDiagnostics: () => ({ entities: [] }),
};

const manager = new CesiumSceneManager(
  () => createEmptyDocument(new Date("2026-07-16T00:00:00Z")),
  port,
  fakeCameraController,
);
await manager.initialize(); // 已注入 port，可不传 viewer
await manager.applySceneOps([{ op: "clear" }]);
```

---

## 6. 例子

### 6.1 清空 → 添加地面站 → 飞过去

```ts
import { CesiumSceneManager } from "./CesiumSceneManager";
import type { SceneOp } from "../contracts/chat";

const manager = new CesiumSceneManager();
await manager.initialize(viewer);

const ops: SceneOp[] = [
  { op: "clear" },
  {
    op: "upsert",
    packets: [
      {
        id: "sanya",
        name: "三亚",
        position: { cartographicDegrees: [109.5, 18.2, 50] },
        point: { pixelSize: 14, color: { rgba: [0, 255, 255, 255] } },
        model: {
          gltf: "/models/facility.glb",
          minimumPixelSize: 64,
        },
      },
    ],
  },
  {
    op: "camera",
    action: "focus",
    targetId: "sanya",
    distanceMeters: 1_000_000,
    pitchDegrees: -40,
  },
];

await manager.applySceneOps(ops);

console.log(manager.buildSummary());
// → {
//   documentClock: { interval, currentTime },
//   entities: [{ id: "sanya", name: "三亚", type: "facility", lon, lat, alt }]
// }
```

### 6.2 只改外观（style），不改轨道

```ts
await manager.applySceneOps([
  {
    op: "style",
    id: "sanya",
    patch: {
      point: { pixelSize: 20, color: { rgba: [255, 200, 0, 255] } },
    },
  },
]);
// position / availability 保持不变；非法 patch（如改 position）会抛错
```

### 6.3 替换轨道（必须 upsert 整包）

```ts
await manager.applySceneOps([
  {
    op: "upsert",
    packets: [
      {
        id: "sat-1",
        name: "SSO-900",
        availability: "2026-07-16T00:00:00Z/2026-07-17T00:00:00Z",
        position: {
          epoch: "2026-07-16T00:00:00Z",
          cartesianVelocity: [/* ... */],
        },
        path: { width: 2, show: true },
        point: { pixelSize: 8 },
        model: { gltf: "/models/satellite.glb", minimumPixelSize: 64 },
      },
    ],
  },
]);
```

若 packet 带 `availability`，SceneManager 可能用其对齐 `document.clock.interval` / `currentTime`，并在 interval 变化时同步 Viewer 时间轴。

### 6.4 删除与选中相关 packet

```ts
await manager.applySceneOps([{ op: "delete", ids: ["sanya"] }]);

sceneManager.setSelectedEntityIds(["sat-1"]);
const packets = sceneManager.pickRelevantPackets(["sat-1", "missing"]);
// 只返回存在的 id；对象为深拷贝
```

### 6.5 相机跟踪（不改文档）

```ts
await manager.applySceneOps([
  { op: "camera", action: "track", targetId: "sat-1" },
]);
// sceneDocument 不变；跟踪状态在 CameraController
```

---

## 7. 设计约束与注意点

1. **先 `initialize` 再 `applySceneOps`**，否则抛错。
2. **并发安全靠队列**，不要绕过 `applySceneOps` 直接改 `sceneDocument`。
3. **模型 URI**：创建时写在 upsert packet（如 `/models/*.glb`）；`style` 禁止设置非 null 的 `gltf`/`uri`/`url`/`image`。
4. **document 不可删、不可被业务 upsert 改写**。
5. **诊断** `getSceneDiagnostics` 主要用于测试开关，不参与业务主路径。

---

## 8. 相关文件

| 文件 | 关系 |
|------|------|
| `sceneDocument.ts` / `sceneDocument.md` | 纯文档归约与 op 语义例子 |
| `sceneStyle.ts` | style patch 白名单与合并 |
| `summary.ts` | 摘要与相关 id |
| `emptyDocument.ts` | 空场景模板 |
| `CesiumCameraController.ts` | camera op |
| `contracts/chat.ts` | `SceneOp` / `ChatRequest` 线格式 |
| `app/App.tsx` | 真实调用编排 |
