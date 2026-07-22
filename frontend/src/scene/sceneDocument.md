假设空文档模板：

```ts
const empty = [
  {
    id: "document",
    name: "CesiumAI Scene",
    version: "1.0",
    clock: { interval: "2026-07-16T00:00:00.000Z/2026-07-17T00:00:00.000Z", currentTime: "2026-07-16T00:00:00.000Z", multiplier: 60 },
  },
];
```

---

### 1. `upsert`：按 id 追加或整包替换

若要改 `position`、`availability` 等轨道/业务字段，只能通过 `upsert` **整包替换**该实体；

`style` 只打视觉补丁，不能改这些字段。

**当前文档：**

```ts
[
  { id: "document", /* ... */ },
  { id: "sanya", name: "old", point: { pixelSize: 4 } },
]
```

**ops：**

```ts
[{ op: "upsert", packets: [{ id: "sanya", name: "new", point: { pixelSize: 10 } }] }]
```

**结果：** `sanya` 被整包换成 `name: "new"`；`document` 不动。

若 upsert 一个新 id（如 `beijing`），则 **追加**；若带 `{ id: "document", name: "Hacked" }`，则 **忽略**，document 名称仍是 `"CesiumAI Scene"`。

---



### 2. `delete`：删业务实体，保留 document

```ts
// current: document + sanya + remove-me
[{ op: "delete", ids: ["remove-me"] }]
// → document + sanya；remove-me 消失

[{ op: "delete", ids: ["document"] }]
// → document 仍在（故意删不掉）
```

---



### 3. `clear`：整表回到空文档

```ts
// current: document + sanya + beijing
[{ op: "clear" }]
// → 只有 empty 那一份 document，业务实体全没了
```

---



### 4. `style`：只打视觉补丁，不换整包

只能替换path、point、model等属性字段，不能替换position、availability（使用upert整体更新）

```ts
// current 里已有 sanya
[{ op: "style", id: "sanya", patch: { point: { pixelSize: 16 } } }]
// → sanya 的 point 等字段被 patch 合并；position 等轨道数据保留
```

对 `document` 或对不存在的 id 会抛错。

---



### 5. 按数组顺序串行（顺序很重要）

```ts
reduceSceneDocument(
  [...empty, { id: "before-clear", point: {} }],
  [
    { op: "upsert", packets: [{ id: "staged", point: {} }] }, // 先加上
    { op: "clear" },                                           // 再清空 → staged 也没了
    { op: "upsert", packets: [{ id: "after-clear", point: {} }] },
    { op: "delete", ids: ["after-clear"] },
  ],
  empty,
);
// 最终几乎只剩 document（after-clear 也被删了）
```

---



### 6. `camera`：在本文件会直接抛错

```ts
reduceSceneDocument(empty, [{ op: "camera", action: "focus", targetId: "sanya" }], empty);
// → Error: 相机 SceneOp 尚未支持…
```

相机由 `CesiumSceneManager` 交给 `CesiumCameraController`，**不走**文档归约。

---



### 和真实聊天路径的关系

后端返回类似：

```json
{
  "sceneOps": [
    { "op": "upsert", "packets": [{ "id": "sanya", "position": { "cartographicDegrees": [109.5, 18.2, 0] }, "point": { "pixelSize": 10 } }] },
    { "op": "camera", "action": "focus", "targetId": "sanya" }
  ]
}
```

`App` 调用 `sceneManager.applySceneOps(sceneOps)` 时：

1. **upsert** → 先 `reduceSceneDocument` 改内存，再更新球上 `CzmlDataSource`
2. **camera** → 跳过文档归约，直接飞到 `sanya`

更完整的用例见 `frontend/src/scene/sceneDocument.test.ts`。