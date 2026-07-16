# Cesium 相机、通用轨道与实体样式实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在现有自然语言场景管线中加入 Cesium 相机控制、skill 驱动的任意传播器建星、ISS 默认 SGP4 流程及安全的通用实体样式修改。

**Architecture:** 后端新增类型化 `camera`/`style` SceneOp、通用 Astrox `/Propagator/*` 调用与标准 Position 校验，仍由 C# Tool 掌握执行权。前端继续持有完整 CZML document，通过独立相机控制器执行相对视角动作，通过白名单深合并把样式 patch 转成完整 packet 后更新 Cesium。

**Tech Stack:** .NET 10、ASP.NET Core、Microsoft Agent Framework、xUnit、React 19、TypeScript、CesiumJS、Vitest、Playwright。

## Global Constraints

- 前端是完整 CZML scene document 的唯一权威。
- 所有场景、样式和相机变更必须来自结构化 Tool/SceneOp，不执行助手文本中的 CZML 或 JavaScript。
- Agent 使用 skills 决定 Astrox 传播器路径与请求体；大型 Position 不在模型上下文中往返。
- 通用 Astrox Tool 只允许 `/Propagator/*` 相对路径。
- 样式 patch 只允许 `point`、`path`、`label`、`billboard`、`model`、`polyline`、`polygon`、`ellipse`，禁止修改身份、位置、时间和 properties。
- “国际空间站”默认 NORAD 25544、最新 TLE、SGP4、未来 24 小时、60 秒步长。
- 保留 `AddSatelliteJ2` 和现有 `clear`、`upsert`、`delete` 行为。
- 所有自动化测试禁止访问 live LLM 或 Astrox。
- 代码注释、文档和提交信息使用中文。

---

### Task 1: 扩展 SceneOp 契约与后端场景 Tools

**Files:**
- Modify: `backend/CesiumAI.Api/Models/SceneOps.cs`
- Create: `backend/CesiumAI.Api/Models/CameraCommands.cs`
- Create: `backend/CesiumAI.Api/Services/SceneStyleValidator.cs`
- Modify: `backend/CesiumAI.Api/Tools/SceneTools.cs`
- Modify: `backend/CesiumAI.Api.Tests/Models/SceneOpSerializationTests.cs`
- Modify: `backend/CesiumAI.Api.Tests/Tools/SceneToolsTests.cs`
- Create: `backend/CesiumAI.Api.Tests/Services/SceneStyleValidatorTests.cs`

**Interfaces:**
- Produces: `CameraSceneOp`, `StyleSceneOp`, `CameraAction`。
- Produces: `ISceneStyleValidator.ValidateAndClone(JsonElement): JsonElement`。
- Produces Tools: `FocusEntity`、`TrackEntity`、`StopTracking`、`AdjustCamera`、`OrbitEntity`、`StopOrbit`、`UpdateEntityStyle`。

- [ ] **Step 1: 编写失败的 SceneOp 序列化测试**

测试 wire shape：

```csharp
SceneOp[] operations =
[
    new CameraSceneOp(
        CameraAction.Focus,
        TargetId: "iss",
        DistanceMeters: 2_000_000,
        HeadingDegrees: 15,
        PitchDegrees: -30),
    new StyleSceneOp(
        "iss",
        JsonSerializer.SerializeToElement(new { path = new { width = 5 } }))
];
```

断言 JSON 包含 `"op":"camera"`、`"action":"focus"`、`"targetId":"iss"`、`"op":"style"`、`"patch"`；既有三种 op 的序列化保持不变。

Run: `dotnet test CesiumAI.slnx --filter "SceneOpSerializationTests"`

Expected: FAIL，因为新类型尚不存在。

- [ ] **Step 2: 实现契约**

`CameraAction` 使用小写字符串 JSON 值：

```csharp
public enum CameraAction
{
    Focus,
    Track,
    Untrack,
    Zoom,
    Pan,
    Rotate,
    OrbitStep,
    OrbitStart,
    OrbitStop
}
```

`CameraSceneOp` 的可选参数包括 `TargetId`、`DistanceMeters`、`HeadingDegrees`、`PitchDegrees`、`RollDegrees`、`Amount`、`Direction`、`AngularSpeedDegreesPerSecond`。`StyleSceneOp` 包含 `Id` 与 `Patch`。在 `SceneOp` 上注册两个新的 `JsonDerivedType`。

Run: `dotnet test CesiumAI.slnx --filter "SceneOpSerializationTests"`

Expected: PASS。

- [ ] **Step 3: 编写失败的样式验证器测试**

覆盖：

- 接受 `{ "point": { "color": { "rgba": [255,0,0,255] } }, "path": { "width": 5 } }`。
- 接受允许属性中的 `null` 删除值。
- 拒绝 `id`、`position`、`availability`、`properties` 与未知顶层键。
- 拒绝 RGBA 长度不为 4、分量不在 0..255、非有限数值、负 `width`/`pixelSize`。
- 拒绝超过 32 KiB、嵌套深度超过 12、数组长度超过 4096。
- 返回 clone，不引用调用方的 `JsonDocument` 生命周期。

Run: `dotnet test CesiumAI.slnx --filter "SceneStyleValidatorTests"`

Expected: FAIL，因为验证器尚不存在。

- [ ] **Step 4: 实现样式验证器**

实现：

```csharp
public interface ISceneStyleValidator
{
    JsonElement ValidateAndClone(JsonElement patch);
}
```

递归遍历 JSON；顶层键使用固定 `HashSet<string>`；任意名为 `rgba` 的数组执行颜色校验；任意名为 `width`、`outlineWidth`、`pixelSize`、`scale` 的数值必须有限且非负；限制序列化 UTF-8 大小、深度和数组长度。

Run: `dotnet test CesiumAI.slnx --filter "SceneStyleValidatorTests"`

Expected: PASS。

- [ ] **Step 5: 编写失败的相机和样式 Tool 测试**

断言每个 Tool 只写一个对应 `CameraSceneOp`/`StyleSceneOp`，并覆盖：

- blank/document id 被拒绝。
- focus 距离必须大于 0。
- zoom amount 不能为 0。
- pan direction 只接受 `left|right|up|down`。
- orbit mode 只接受 `step|start`，角速度大于 0。
- `UpdateEntityStyle` 解析 JSON 字符串并调用验证器；非法 JSON 或非法 patch 不写 op。

Run: `dotnet test CesiumAI.slnx --filter "SceneToolsTests"`

Expected: FAIL，因为 Tools 尚不存在。

- [ ] **Step 6: 实现 Tools 并回归后端测试**

每个 Tool 使用 `[Description]`，参数使用度和米，返回简洁结果文本。`UpdateEntityStyle(string id, string patchJson)` 只在验证成功后写 op。

Run: `dotnet test CesiumAI.slnx --filter "SceneToolsTests|SceneStyleValidatorTests|SceneOpSerializationTests"`

Expected: PASS。

- [ ] **Step 7: 提交**

```bash
git add backend/CesiumAI.Api/Models backend/CesiumAI.Api/Services/SceneStyleValidator.cs backend/CesiumAI.Api/Tools/SceneTools.cs backend/CesiumAI.Api.Tests
git commit -m "功能：增加相机与实体样式场景操作"
```

---

### Task 2: 实现通用 Astrox Position 管线

**Files:**
- Modify: `backend/CesiumAI.Api/Astrox/AstroxContracts.cs`
- Modify: `backend/CesiumAI.Api/Astrox/AstroxClient.cs`
- Create: `backend/CesiumAI.Api/Astrox/CzmlPositionValidator.cs`
- Modify: `backend/CesiumAI.Api/Astrox/OrbitScenarioService.cs`
- Modify: `backend/CesiumAI.Api/Tools/SceneTools.cs`
- Modify: `backend/CesiumAI.Api.Tests/Astrox/AstroxClientTests.cs`
- Create: `backend/CesiumAI.Api.Tests/Astrox/CzmlPositionValidatorTests.cs`
- Modify: `backend/CesiumAI.Api.Tests/Astrox/OrbitScenarioServiceTests.cs`
- Modify: `backend/CesiumAI.Api.Tests/Tools/SceneToolsTests.cs`

**Interfaces:**
- Produces: `IAstroxClient.PropagateAsync(string endpoint, JsonElement request, CancellationToken): Task<GenericPropagationResponse>`。
- Produces: `ICzmlPositionValidator.ValidateAndClone(JsonElement position, DateTimeOffset start, DateTimeOffset stop): JsonElement`。
- Produces: `IOrbitScenarioService.CreatePacketFromPropagationAsync(...)` 与 `CreatePacketFromPositions(...)`。
- Produces Tools: `PropagateAndAddSatellite`、`AddSatelliteFromPositions`。

- [ ] **Step 1: 编写失败的通用 Astrox HTTP 测试**

覆盖：

- `/Propagator/TwoBody` 请求 JSON 原样发送，响应 `Position` 被 clone。
- `/Propagator/SGP4` 同样可用，证明客户端不枚举传播器。
- 拒绝绝对 URL、`..`、query 中改变路径语义、非 `/Propagator/` 路径。
- HTTP 非 2xx、空 body、非法 JSON、`IsSuccess=false`、缺失 Position 都抛 `AstroxException`。

Run: `dotnet test CesiumAI.slnx --filter "AstroxClientTests"`

Expected: FAIL，因为 `PropagateAsync` 尚不存在。

- [ ] **Step 2: 实现通用 Astrox 调用**

增加：

```csharp
public sealed record GenericPropagationResponse(
    bool IsSuccess,
    string Message,
    JsonElement Position,
    double? Period) : IAstroxSuccessResponse;
```

将 endpoint 规范化为以 `/Propagator/` 开头、无 authority、无 fragment 的相对 URI；用 `JsonContent.Create(request)` POST；复用现有错误读取和成功 payload 校验。

Run: `dotnet test CesiumAI.slnx --filter "AstroxClientTests"`

Expected: PASS。

- [ ] **Step 3: 编写失败的 Position 验证测试**

覆盖：

- 接受 epoch + stride 4 `cartesian`。
- 接受 epoch + stride 7 `cartesianVelocity`。
- 拒绝缺 epoch、无采样字段、空数组、错误 stride、NaN/Infinity、负或重复时间偏移、超出 start/stop。
- 拒绝同时携带两种采样字段。
- 限制 24 小时、最多 10000 个样本、position JSON 最大 2 MiB。

Run: `dotnet test CesiumAI.slnx --filter "CzmlPositionValidatorTests"`

Expected: FAIL，因为验证器尚不存在。

- [ ] **Step 4: 实现 Position 验证器**

解析 epoch 为 UTC；按 stride 读取每个首元素时间偏移并验证严格递增及 availability 范围；所有数值使用 `double.IsFinite`；成功时返回独立 clone。

Run: `dotnet test CesiumAI.slnx --filter "CzmlPositionValidatorTests"`

Expected: PASS。

- [ ] **Step 5: 编写失败的通用 packet 与 Tool 测试**

使用固定 start/stop、Position、名称和 orbit hint，断言完整 packet 含 availability、position、默认 point/path、trailTime 和 `properties.orbitHint.string`。断言传播/验证失败不写 op。直接 positions Tool 使用同一 packet builder。

Run: `dotnet test CesiumAI.slnx --filter "OrbitScenarioServiceTests|SceneToolsTests"`

Expected: FAIL，因为通用服务和 Tools 尚不存在。

- [ ] **Step 6: 实现通用 packet 与 Tools**

`PropagateAndAddSatellite` 参数：

```csharp
Task<string> PropagateAndAddSatellite(
    string id,
    string? name,
    string propagatorPath,
    string requestJson,
    string startUtc,
    string stopUtc,
    string? orbitHint = null,
    CancellationToken cancellationToken = default);
```

`AddSatelliteFromPositions` 参数：

```csharp
string AddSatelliteFromPositions(
    string id,
    string? name,
    string positionJson,
    string startUtc,
    string stopUtc,
    string? orbitHint = null);
```

两者必须解析并校验 UTC、最长 24 小时、完整 JSON 和 Position；仅成功后写一个 `UpsertSceneOp`。

Run: `dotnet test CesiumAI.slnx --filter "AstroxClientTests|CzmlPositionValidatorTests|OrbitScenarioServiceTests|SceneToolsTests"`

Expected: PASS。

- [ ] **Step 7: 提交**

```bash
git add backend/CesiumAI.Api/Astrox backend/CesiumAI.Api/Tools/SceneTools.cs backend/CesiumAI.Api.Tests
git commit -m "功能：增加通用轨道传播与建星管线"
```

---

### Task 3: 实现前端样式合并

**Files:**
- Modify: `frontend/src/contracts/chat.ts`
- Create: `frontend/src/scene/sceneStyle.ts`
- Modify: `frontend/src/scene/sceneDocument.ts`
- Modify: `frontend/src/scene/CesiumSceneManager.ts`
- Modify: `frontend/src/contracts/chat.test.ts`
- Create: `frontend/src/scene/sceneStyle.test.ts`
- Modify: `frontend/src/scene/sceneDocument.test.ts`
- Modify: `frontend/src/scene/CesiumSceneManager.test.ts`

**Interfaces:**
- Produces: TypeScript `CameraSceneOp`、`StyleSceneOp`。
- Produces: `applyStylePatch(packet, patch): CzmlPacket`。

- [ ] **Step 1: 编写失败的前端契约与 style reducer 测试**

测试：

```ts
const operations: SceneOp[] = [
  { op: "camera", action: "track", targetId: "iss" },
  { op: "style", id: "iss", patch: { path: { width: 5 } } },
];
```

style reducer 断言深合并后 `position`、`availability`、`properties` 不变；对象递归合并、数组替换、`null` 删除允许字段；拒绝 document、不存在实体、禁用字段和未知顶层键。

Run: `cd frontend && npm test -- --run src/contracts/chat.test.ts src/scene/sceneStyle.test.ts src/scene/sceneDocument.test.ts`

Expected: FAIL，因为契约和 reducer 尚不存在。

- [ ] **Step 2: 实现前端契约和双重样式验证**

`SceneOp` 加入：

```ts
export type CameraSceneOp = {
  op: "camera";
  action: "focus" | "track" | "untrack" | "zoom" | "pan" | "rotate"
    | "orbitStep" | "orbitStart" | "orbitStop";
  targetId?: string;
  distanceMeters?: number;
  headingDegrees?: number;
  pitchDegrees?: number;
  rollDegrees?: number;
  amount?: number;
  direction?: "left" | "right" | "up" | "down";
  angularSpeedDegreesPerSecond?: number;
};

export type StyleSceneOp = {
  op: "style";
  id: string;
  patch: Record<string, unknown>;
};
```

`sceneStyle.ts` 镜像后端顶层白名单和核心数值/RGBA 校验，并实现不可变深合并。

Run: `cd frontend && npm test -- --run src/contracts/chat.test.ts src/scene/sceneStyle.test.ts`

Expected: PASS。

- [ ] **Step 3: 将 style 接入 canonical document 与 manager**

`reduceSceneDocument` 的 `style` 分支找到目标完整 packet，调用 `applyStylePatch`；`CesiumSceneManager` 先计算 next packet，移除同 ID Entity，再 `process([completePacket])`，失败时沿用 upsert rollback，成功后提交 document。

Run: `cd frontend && npm test -- --run src/scene/sceneDocument.test.ts src/scene/CesiumSceneManager.test.ts`

Expected: PASS，且测试明确检查更新宽度后动态 position 仍存在。

- [ ] **Step 4: 提交**

```bash
git add frontend/src/contracts frontend/src/scene
git commit -m "功能：增加安全的实体样式深度合并"
```

---

### Task 4: 实现 Cesium 相机控制器

**Files:**
- Create: `frontend/src/scene/CesiumCameraController.ts`
- Create: `frontend/src/scene/CesiumCameraController.test.ts`
- Modify: `frontend/src/scene/CesiumSceneManager.ts`
- Modify: `frontend/src/scene/CesiumSceneManager.test.ts`
- Modify: `frontend/src/components/ViewerHost.tsx`

**Interfaces:**
- Produces: `CameraControllerPort.apply(operation): Promise<void>`。
- Produces: `CameraControllerPort.onSceneCleared()`、`onEntitiesDeleted(ids)`、`destroy()`。

- [ ] **Step 1: 编写失败的相机控制器测试**

使用小型 Viewer/Entity adapter fake，覆盖：

- focus 查找目标并 flyTo。
- track/untrack 设置和清除 tracked entity。
- zoom/pan/rotate 调用相对 camera API。
- orbitStep 使用当前时钟目标位置并只执行一次。
- orbitStart 只注册一个 tick listener；每 tick 更新动态目标中心与 heading。
- track 与 orbit 互斥。
- orbitStop、clear、目标删除和 destroy 都移除 listener。
- 目标不存在或当前时间无 position 时抛错且无泄漏。

Run: `cd frontend && npm test -- --run src/scene/CesiumCameraController.test.ts`

Expected: FAIL，因为控制器尚不存在。

- [ ] **Step 2: 实现相机控制器**

生产 adapter 使用 Cesium：

- `viewer.flyTo(entity, { offset: new HeadingPitchRange(...) })`
- `viewer.trackedEntity`
- `viewer.camera.zoomIn/zoomOut`
- `viewer.camera.moveLeft/moveRight/moveUp/moveDown`
- `viewer.camera.lookLeft/lookRight/lookUp/lookDown/twistLeft/twistRight`
- `viewer.clock.onTick.addEventListener`
- `entity.position?.getValue(viewer.clock.currentTime)`
- `Transforms.eastNorthUpToFixedFrame` 与 `camera.lookAtTransform`

持续环绕保存 unsubscribe、目标 ID、heading、pitch、range、角速度和上一 tick 时间；每次启动前调用统一 `stopOrbit()`。

Run: `cd frontend && npm test -- --run src/scene/CesiumCameraController.test.ts`

Expected: PASS。

- [ ] **Step 3: 接入 SceneManager 生命周期**

生产初始化时用同一 Viewer 和 `CzmlDataSource.entities.getById` 构造控制器。`camera` op 按序 await controller；clear 前停止控制，delete 目标时清理。ViewerHost cleanup 调用 manager `destroy()`，再销毁 Viewer。

Run: `cd frontend && npm test -- --run src/scene/CesiumCameraController.test.ts src/scene/CesiumSceneManager.test.ts src/components/ViewerHost.test.tsx`

Expected: PASS。

- [ ] **Step 4: 提交**

```bash
git add frontend/src/scene frontend/src/components/ViewerHost.tsx
git commit -m "功能：增加 Cesium 相机跟随与环绕控制"
```

---

### Task 5: 注册 Agent Tools、ISS 默认策略与端到端验收

**Files:**
- Modify: `backend/CesiumAI.Api/Services/AgentFactory.cs`
- Modify: `backend/CesiumAI.Api/Services/AgentInstructions.cs`
- Modify: `backend/CesiumAI.Api/Program.cs`
- Modify: `backend/CesiumAI.Api.Tests/Services/AgentFactoryTests.cs` if present, otherwise create it
- Modify: `backend/CesiumAI.Api.Tests/Services/AgentInstructionsTests.cs` if present, otherwise create it
- Modify: `frontend/e2e/scene-chat.spec.ts`
- Modify: `README.md`

**Interfaces:**
- Registers all new scene Tools with the Agent.
- Documents deterministic ISS defaults and generic propagator policy.
- Produces browser acceptance evidence for camera, ISS and style flows.

- [ ] **Step 1: 编写失败的 Agent wiring/instruction 测试**

断言 Agent tool list包含七个相机/样式 Tool 与两个通用轨道 Tool。断言 instructions 包含：

- 非 J2 轨道先加载 skill。
- 通用传播 Tool 直接消费 Position，禁止大型 Position 在模型中往返。
- ISS 默认 `25544`、最新 TLE、`SGP4`、`24` 小时、`60` 秒。
- 查询 TLE 使用现有受限 `HttpGet`，再把小型 TLE/request 交给通用传播 Tool。

Run: `dotnet test CesiumAI.slnx --filter "AgentFactoryTests|AgentInstructionsTests"`

Expected: FAIL，因为注册和策略尚未更新。

- [ ] **Step 2: 注册依赖和 Tools**

在 `Program.cs` 注册 `ISceneStyleValidator`、`ICzmlPositionValidator`；在 `AgentFactory` 将所有新 `SceneTools` 方法包装为 `AIFunctionFactory.Create(...)`。更新 `AgentInstructions.Text`，删除“AddSatelliteJ2 是唯一途径”。

Run: `dotnet test CesiumAI.slnx`

Expected: PASS。

- [ ] **Step 3: 扩展 Playwright 验收**

在现有 mock `/api/chat` 中增加确定性响应：

1. 建立地面站和动态 ISS packet。
2. “定位到地面站”返回 focus。
3. “跟随国际空间站”返回 track。
4. “停止跟随并持续环绕”返回 untrack + orbitStart。
5. “停止环绕”返回 orbitStop。
6. “把国际空间站改成红色，轨迹宽度 5”返回 style。

通过 `window` 上现有测试诊断接口或扩展只读 diagnostics，断言 tracked entity、orbit active、相机位置发生变化、完整 Position 仍存在、path width 与 point color 已更新；继续断言 Cesium canvas 持久存在且无 console error。

Run: `cd frontend && npm run e2e`

Expected: 所有场景 PASS。

- [ ] **Step 4: 更新文档并运行完整验证**

README 增加自然语言示例、通用传播器行为、ISS 默认值、样式白名单和相机命令。

Run:

```bash
dotnet test CesiumAI.slnx
cd frontend && npm test -- --run
cd frontend && npm run typecheck
cd frontend && npm run lint
cd frontend && npm run build
cd frontend && npm run e2e
```

Expected: 全部退出 0。

- [ ] **Step 5: GUI 手工验收**

启动前端，使用 mock 响应依次演示定位、跟随、相对微调、单次环绕、持续环绕/停止和样式修改。录制一段从命令发出到实际 Cesium 行为完成的视频，并保存最终样式截图。

- [ ] **Step 6: 提交**

```bash
git add backend frontend README.md
git commit -m "功能：接入通用轨道、相机与样式自然语言流程"
```

---

## Self-Review

- 设计中的相机、通用轨道、ISS、样式、安全边界、错误处理和测试均映射到具体任务。
- Task 1/2 先建立后端契约，Task 3/4 消费稳定契约，Task 5 完成 Agent 与端到端集成。
- 类型名、Tool 名和默认值在各任务间保持一致。
- 不依赖 live LLM/Astrox 完成自动化验证。
