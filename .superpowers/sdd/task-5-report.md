# Task 5 Report: SceneOpCollector 与强类型场景工具

## 状态

- 已完成 `SceneOpCollector` 与 `SceneTools` 实现。
- 未接入 Agent，保持改动范围仅限 Task 5 指定文件。

## 实现内容

### 1. SceneOpCollector

- 新增 `ISceneOpSink.Add(SceneOp operation)`。
- 新增 `SceneOpCollector.Drain(): IReadOnlyList<SceneOp>`。
- 使用私有锁与私有列表保证 `Add`/`Drain` 线程安全。
- `Drain()` 返回当前快照并清空内部队列。

### 2. SceneTools

- 新增强类型工具：
  - `ClearScene()`
  - `UpsertFacility(...)`
  - `DeleteEntity(string[] ids)`
  - `AddSatelliteJ2(...)`
- 所有公开工具方法均添加 `[Description]`。
- `UpsertFacility(...)`：
  - 校验空白 id、经度范围 `[-180, 180]`、纬度范围 `[-90, 90]`。
  - 构造完整 facility packet，包含 `cartographicDegrees`、`point`、`label`。
- `DeleteEntity(...)`：
  - 过滤空白 id。
  - 过滤保留 id `document`。
  - 去重后仅在存在有效 id 时写入 `DeleteSceneOp`。
- `AddSatelliteJ2(...)`：
  - 默认值为 `900 km / 24 h / 60 s / 10.5`。
  - `epochUtc` 未提供时，使用 `TimeProvider.GetUtcNow()` 向下取整到当前分钟。
  - 成功时将 `IOrbitScenarioService.CreateSsoJ2PacketAsync(...)` 返回的完整 packet 写入 `UpsertSceneOp`。
  - `AstroxException` 或其他上游异常抛出时，不写入任何 scene op。

## 测试

### 聚焦测试

命令：

```bash
dotnet test CesiumAI.slnx --filter "FullyQualifiedName~SceneOpCollectorTests|FullyQualifiedName~SceneToolsTests"
```

结果：12/12 通过。

### 完整后端测试

命令：

```bash
dotnet test CesiumAI.slnx
```

结果：36/36 通过。

## TDD 记录

1. 先新增 `SceneOpCollectorTests`，确认因缺少 `CesiumAI.Api.Services` 而编译失败。
2. 实现 `SceneOpCollector` 最小代码后转绿。
3. 再新增 `SceneToolsTests`，确认因缺少 `CesiumAI.Api.Tools` 而编译失败。
4. 实现 `SceneTools` 最小代码后转绿。
5. 运行 Task 5 聚焦测试与完整后端测试，均通过。

## 文件

- `backend/CesiumAI.Api/Services/SceneOpCollector.cs`
- `backend/CesiumAI.Api/Tools/SceneTools.cs`
- `backend/CesiumAI.Api.Tests/Services/SceneOpCollectorTests.cs`
- `backend/CesiumAI.Api.Tests/Tools/SceneToolsTests.cs`

## 关注点

- 当前仅提供独立 collector 和工具类，尚未注册到任何运行时依赖注入或 Agent 流程中；这符合 Task 5 “不接 Agent”的范围约束。
