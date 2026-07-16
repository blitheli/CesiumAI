# Cesium 相机、通用轨道与实体样式设计

> 状态：已完成对话评审  
> 日期：2026-07-16  
> 关联：`Docs/prd.md`

## 1. 目标

在现有“自然语言 → Agent Tools → 结构化 `sceneOps` → Cesium”的安全边界上增加：

1. 自然语言相机控制，包括定位、跟随、连续微调、单次环绕和持续环绕。
2. 不绑定传播器枚举的通用卫星轨道管线，兼容 TwoBody、J2、HPOP、SGP4 及未来返回标准 positions 的算法。
3. “添加国际空间站”的默认 TLE/SGP4 流程。
4. 对已有实体执行经过白名单校验的通用 CZML 视觉样式修改。

## 2. 设计原则

- 前端继续作为完整 CZML document 的唯一权威。
- 场景变更只通过结构化 Tool 和 `sceneOps` 执行，不解析助手文本中的可执行 CZML。
- Agent 使用 skills 理解 Astrox 端点及请求格式；大型 positions 数据不经过 LLM 二次复制。
- 后端和前端都验证样式 patch，禁止借样式操作修改轨道、时间或实体身份。
- 新能力保持现有 `clear`、`upsert`、`delete` 与 `AddSatelliteJ2` 兼容。

## 3. 总体架构

扩展 `SceneOp` 联合类型：

- `camera`：执行相机定位和相对运动。
- `style`：对指定实体应用视觉属性 patch。
- 现有 `clear`、`upsert`、`delete` 保持不变。

通用轨道数据流：

1. Agent 根据用户意图加载对应 skill。
2. skill 提供 `/Propagator/*` 端点、请求结构、单位和约束。
3. Agent 调用通用传播并建星 Tool，传入端点、请求和卫星展示参数。
4. 后端直接调用 Astrox，并从响应中提取、验证标准 CZML `Position`。
5. 后端组装完整卫星 packet，写入 `UpsertSceneOp`。
6. 前端按现有流程更新 canonical document 和 `CzmlDataSource`。

另提供直接接收标准 CZML position 的建星 Tool，支持非 Astrox 数据源。

## 4. 相机控制

### 4.1 Camera SceneOp

`camera` 操作包含动作名、可选目标实体 ID 和动作参数。支持：

| 动作 | 语义 |
|---|---|
| `focus` | 飞到实体，可指定距离、航向和俯仰 |
| `track` | 设置 `viewer.trackedEntity` |
| `untrack` | 清除当前跟随 |
| `zoom` | 相对当前视角拉近或拉远 |
| `pan` | 按屏幕方向或距离平移 |
| `rotate` | 相对调整航向、俯仰和翻滚 |
| `orbitStep` | 围绕实体单次旋转指定角度 |
| `orbitStart` | 按角速度持续环绕实体 |
| `orbitStop` | 停止持续环绕 |

距离使用米，角度和角速度的 Tool 入参使用度，前端执行前转换为弧度。相对动作不要求后端知道当前相机数值，因此“再拉近一点”“向左转 30 度”等多轮命令可直接映射为增量操作。

### 4.2 模式与生命周期

- `focus` 使用 Cesium `viewer.flyTo`，并支持目标距离、航向、俯仰。
- `track` 使用 Cesium `viewer.trackedEntity`。
- `orbitStart` 注册单一更新回调，以当前 Cesium 时钟下实体位置作为动态中心。
- 启动跟随前停止环绕；启动环绕前清除跟随，防止相机控制器竞争。
- `orbitStop`、场景清空、目标实体删除、Viewer 卸载时必须移除环绕回调。
- 切换到其他环绕目标时先清理旧目标。
- 目标不存在或在当前时刻无有效位置时，操作失败且不得遗留回调。

## 5. 通用轨道管线

### 5.1 Tool 边界

新增两个 Tool：

1. `PropagateAndAddSatellite`
   - 输入卫星 ID、名称、轨道说明、传播器相对路径、请求 JSON 和展示时间参数。
   - 仅允许规范化后位于 `/Propagator/` 下的相对路径。
   - 后端调用 Astrox 并直接消费响应，不把完整 positions 返回给模型再提交。
2. `AddSatelliteFromPositions`
   - 输入卫星信息和标准 CZML position JSON。
   - 用于已经由其他可信数据源生成 positions 的场景。

`AddSatelliteJ2` 保留，以免破坏已有调用和测试；Agent 指令改为优先使用 skill 驱动的通用 Tool，不再宣称 J2 是唯一建星路径。

### 5.2 Position 验证

通用服务接受包含有效 `epoch` 且具有以下任一采样字段的 position：

- `cartesian`
- `cartesianVelocity`

验证至少包括：

- position 必须是 JSON 对象。
- `epoch` 必须是有效 UTC 时间。
- 采样字段必须是有限数值数组，元组长度符合对应 schema。
- 时间偏移必须单调、不重复，并位于声明的 availability 范围内。
- 样本数量、JSON 大小、传播时长和步长有上限，防止异常响应耗尽内存。
- Astrox HTTP 非成功、空响应、`IsSuccess=false` 或缺少 Position 时抛出可读错误。

校验完成后，轨道服务生成包含 `id`、`name`、`availability`、`position`、默认 `point`、默认 `path` 和 `properties.orbitHint` 的完整 packet。

### 5.3 国际空间站

用户仅说“添加国际空间站”时采用以下默认值：

- NORAD Catalog Number：`25544`
- 先查询最新 TLE
- 传播器：SGP4
- 起点：当前 UTC 时间截断到分钟
- 时长：未来 24 小时
- 步长：60 秒

用户明确指定时长或步长时可覆盖对应默认值。TLE 查询失败、结果不唯一或响应缺少两行根数时，不调用传播器，也不产生 `sceneOps`。

## 6. 通用实体样式

### 6.1 Style SceneOp

`style` 操作包含：

- `id`：目标业务实体 ID。
- `patch`：经过后端验证的视觉 CZML patch。

允许的顶层视觉属性：

- `point`
- `path`
- `label`
- `billboard`
- `model`
- `polyline`
- `polygon`
- `ellipse`

明确禁止：

- 修改 `document` packet。
- 修改或注入 `id`、`position`、`availability`、`properties`。
- 使用未知顶层属性。
- 超出限制的对象深度、数组长度或 JSON 总大小。
- 非有限数值、非法 RGBA、负宽度或负像素尺寸。

模型可以提出小型视觉 patch，但只有 C# Tool 校验并写入 `StyleSceneOp` 后才具有执行权。

### 6.2 合并语义

前端根据 ID 读取 canonical document 中的完整 packet，然后：

1. 再次执行同一白名单验证。
2. 对允许的对象属性递归合并。
3. 数组整体替换。
4. `null` 删除对应的允许视觉属性。
5. 保留所有非视觉字段。
6. 以合并后的完整 packet 更新 canonical document 和 Cesium。

这一语义修复当前局部 upsert 可能导致 canonical packet 丢失 `position` 的问题，并保证后续 `sceneSummary` 和 `relevantPackets` 仍完整。

## 7. 顺序、一致性与错误处理

- 一轮响应中的所有 `sceneOps` 严格按数组顺序执行。
- 相机操作可以引用同一轮更早 `upsert` 创建的实体。
- 每个操作只在对应 Cesium 调用成功后提交 canonical document 状态。
- 前端在第一个失败操作处停止，保留此前成功操作的状态，并向用户显示错误。
- 目标实体不存在、样式 patch 非法、传播失败或 position 校验失败时，后端 Tool 不写入操作。
- 场景清空和实体删除必须同步清理相关跟随/环绕状态。

## 8. Agent 指令

Agent 系统指令更新为：

1. 所有场景、样式和相机变更必须调用对应 Tool。
2. 禁止在助手文本中输出可执行 CZML 作为执行手段。
3. 创建非现有 J2 快捷场景时，先加载对应 skill，再调用通用传播并建星 Tool。
4. 不得让大型 positions 在工具结果与模型参数间往返；Astrox 传播使用一体化 Tool。
5. “国际空间站”无其他限定时使用 NORAD 25544、最新 TLE、SGP4、24 小时和 60 秒步长。
6. 纯问答不产生 `sceneOps`。

## 9. 测试策略

### 9.1 后端

- `SceneOp` 序列化覆盖所有 camera 动作和 style。
- 相机 Tool 参数范围、目标要求和 operation 收集。
- 样式白名单、递归结构、颜色、数值、大小限制及拒绝路径。
- 通用 Astrox 路径限制、请求透传、成功 Position 提取和所有失败分支。
- position 的两种采样格式、非法 tuple、非单调时间和 availability 越界。
- ISS 查询使用 NORAD 25544，并将最新 TLE 交给 SGP4 请求。
- 现有 `AddSatelliteJ2` 回归测试继续通过。

所有后端自动化测试使用 fake HTTP handler，不访问 live LLM 或 Astrox。

### 9.2 前端

- SceneOp 运行时契约验证。
- `focus`、跟随切换、缩放、平移和旋转。
- 单次环绕、持续环绕、动态目标更新和所有清理路径。
- style 深度合并、数组替换、`null` 删除、二次白名单校验。
- style 更新后 position、availability 和 properties 保持不变。
- 操作排序和失败停止行为。

### 9.3 浏览器验收

Playwright 使用确定性 mock `POST /api/chat`，覆盖：

1. “定位到地面站”。
2. “跟随卫星”及停止跟随。
3. 单次环绕和持续环绕及停止。
4. 添加 ISS/SGP4 卫星。
5. 修改实体颜色、点大小和轨迹粗细。

实际 GUI 手工验收需录制视频，展示相机运动、动态跟随/环绕和样式变化。仅在凭据与 Astrox 可用时补充 live smoke；自动化验收不依赖外部服务。

## 10. 非目标

- 不允许执行 Agent 生成的任意 JavaScript。
- 不开放任意 CZML 字段 patch。
- 不在后端持久化完整场景或相机状态。
- 不为每种传播器新增前端分支或专用 SceneOp。
- 不在本期引入 SSE、WebSocket 或多人协作。

## 11. 验收标准

- 用户可通过自然语言定位、跟随、停止跟随和连续微调相机。
- 用户可对静态或动态实体执行单次及持续环绕，并可靠停止。
- Agent 可根据 skills 使用 TwoBody、J2、HPOP、SGP4 等返回标准 Position 的传播器建星，前端无需感知传播器类型。
- “添加国际空间站”默认执行 NORAD 25544 → 最新 TLE → SGP4 → 24 小时/60 秒 positions。
- 用户可通过自然语言修改允许的实体视觉属性，轨道和其他非视觉数据保持完整。
- 非法路径、响应、positions 或样式 patch 不改变场景。
- 后端、前端和 Playwright 测试通过，GUI 视频证明 Cesium 实际行为符合设计。
