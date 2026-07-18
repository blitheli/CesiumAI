# Cesium 控件、默认模型与鼠标解除环绕设计

> 状态：已完成对话评审  
> 日期：2026-07-17  
> 关联：`Docs/prd.md`、`Docs/superpowers/specs/2026-07-16-cesium-camera-generic-orbits-style-design.md`

## 1. 目标

1. 持续环绕激活时，用户用鼠标旋转、平移或缩放即解除环绕，交还手动相机控制。
2. Viewer 初始启动显示接近 Cesium 默认的典型 widgets。
3. 地面站与卫星实体默认加载同源 glTF：`/models/facility.glb`、`/models/satellite.glb`。

## 2. 决策记录

| 项 | 决策 |
|---|---|
| 解除环绕手势 | 左键拖拽旋转、中键/右键平移、滚轮缩放 |
| 不解除 | 点击选中实体、悬停 |
| Widgets | 接近默认全套（含 geocoder、baseLayerPicker）；关闭 vrButton |
| Ion token | 可选 `VITE_CESIUM_ION_TOKEN`；未配置时控件仍显示 |
| 模型文件 | 仓库提交轻量占位 glb，可被同名文件替换 |
| 样式安全 | 仍禁止任意外部模型/图片 URL；默认同源路径由创建 Tool 写入 |

## 3. 鼠标解除环绕

在 `CesiumCameraController` 中：

- `orbitStart` 成功后注册 `ScreenSpaceEventHandler`（或等价 adapter 钩子）。
- 监听：`LEFT_DOWN`+移动（旋转）、`MIDDLE_DOWN`/`RIGHT_DOWN`+移动（平移）、`WHEEL`（缩放）。
- 任一手势触发即调用现有 `stopOrbit()`，清除 lookAt 约束，恢复用户输入控制。
- `orbitStop`、场景清空、目标删除、`destroy` 时移除 handler。
- `track` 不受影响，除非同时处于环绕（环绕优先被解除）。

测试用 fake adapter 覆盖：环绕中模拟轮/拖拽 → unsubscribe；非环绕时监听未激活。

## 4. 典型 Widgets

修改 `ViewerHost`：

- 开启：`animation`、`timeline`、`baseLayerPicker`、`fullscreenButton`、`geocoder`、`homeButton`、`infoBox`、`sceneModePicker`、`selectionIndicator`、`navigationHelpButton`。
- 关闭：`vrButton`。
- 若 `import.meta.env.VITE_CESIUM_ION_TOKEN` 非空，设置 `Ion.defaultAccessToken`。
- 移除强制 `baseLayer: false` 与手动 NaturalEarthII 注入，交由默认/`baseLayerPicker` 管理。
- 更新 `ViewerHost` 测试与 README / `vite-env.d.ts`。

## 5. 默认 glTF 模型

- 新增 `frontend/public/models/facility.glb`、`frontend/public/models/satellite.glb`（轻量占位几何体）。
- `UpsertFacility` 默认 packet 增加：
  ```json
  "model": {
    "gltf": "/models/facility.glb",
    "minimumPixelSize": 64,
    "maximumScale": 20000
  }
  ```
  保留现有 `point`/`label` 作为兜底标识。
- `OrbitScenarioService.BuildSatellitePacket` 默认增加：
  ```json
  "model": {
    "gltf": "/models/satellite.glb",
    "minimumPixelSize": 64,
    "maximumScale": 20000
  }
  ```
  保留 `point`/`path`。
- 样式白名单继续禁止 `billboard`/`model` 内非 null 的任意外部 `uri`/`url`/`gltf`/`image`；创建路径写入的固定同源默认值不经过 style patch。
- 更新后端 SceneTools / OrbitScenarioService 测试与前端/e2e 期望。

## 6. 测试与验收

- 单元：鼠标解除环绕；Viewer 选项与 ion token；facility/satellite packet 含默认 model。
- e2e：场景仍可添加地面站/卫星；可选断言 packet/diagnostics 含 model.gltf。
- GUI：环绕中滚轮/拖拽停止；控件可见；实体显示占位模型。

## 7. 非目标

- 不强制要求 ion token 才能启动。
- 不开放样式 patch 设置任意远程模型 URL。
- 不把 `track` 模式改为鼠标解除。
*** End Patch
