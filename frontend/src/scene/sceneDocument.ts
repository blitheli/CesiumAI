/**
 * 将一组 SceneOp 应用到当前 CZML 文档，返回新文档（不修改入参 `current`）。
 * 
 * 纯 CZML 文档归约：只改内存中的 packet 数组，不碰 Cesium / WebGL。
 *
 * 核心入口 `reduceSceneDocument(current, ops, emptyDocument)` 按序解释 SceneOp：
 * - `clear` → 换成空文档副本
 * - `upsert` → 按 id 替换或追加（忽略业务侧对 `document` 的 upsert）
 * - `delete` → 删实体，始终保留 `document`
 * - `style` → 调 `sceneStyle.applyStylePatch` 打补丁
 * - `camera` → 抛错（相机不改文档，由 `CesiumSceneManager` 交给相机控制器）
 *
 * 与 SceneManager 拆分便于单测：文档逻辑与渲染解耦。详见 Docs/前端说明.md §4.7。
 */
import type { CzmlPacket, SceneOp } from "../contracts/chat";
import { applyStylePatch } from "./sceneStyle";

/** 深拷贝单个 packet，避免归约过程污染调用方持有的引用。
 * 
 * structuredClone 是 JavaScript 原生内置的深拷贝函数。
 * 它基于结构化克隆算法，能安全克隆复杂数据类型（如 Date、Set、Map、循环引用等），提供简单、高性能的深拷贝方案
 */
function clonePacket(packet: CzmlPacket): CzmlPacket {
  return structuredClone(packet);
}

/** 深拷贝整份文档（packet 数组）。 */
function cloneDocument(document: CzmlPacket[]): CzmlPacket[] {
  return document.map(clonePacket);
}

/** upsert 的 packet.id 必须是非空字符串。 */
function assertValidUpsertId(id: unknown): asserts id is string {
  if (typeof id !== "string" || id.trim() === "") {
    throw new Error("Upsert packet id must be a non-empty string");
  }
}

/**
 * 对指定实体应用 style patch（原地改 `working`）。
 * 禁止对 `document` 打样式；目标不存在则抛错。
 */
function applyStyleOperation(
  working: CzmlPacket[],
  id: string,
  patch: Record<string, unknown>,
): void {
  if (id === "document") {
    throw new Error("不能对 document packet 应用样式。");
  }

  const index = working.findIndex((packet) => packet.id === id);
  if (index < 0) {
    throw new Error(`样式目标实体不存在：'${id}'。`);
  }

  working[index] = applyStylePatch(working[index]!, patch);
}

/**
 * 将一批 SceneOp 归约到当前 CZML 文档，返回新文档（不修改入参 `current`）。
 *
 * @param current 当前内存文档（含 `document` packet）
 * @param operations 待应用的 ops；其中 `camera` 在此层不受理
 * @param emptyDocument clear 时使用的空场景模板（通常来自 `emptyDocument.ts`）
 */
export function reduceSceneDocument(
  current: CzmlPacket[],
  operations: SceneOp[],
  emptyDocument: CzmlPacket[],
): CzmlPacket[] {
  // 深拷贝当前文档，避免修改入参
  let working = cloneDocument(current);

  for (const operation of operations) {
    switch (operation.op) {
      case "clear":
        // 丢弃全部业务实体，回到空文档模板。
        working = cloneDocument(emptyDocument);
        break;
      // upsert 按 id 整包替换
      case "upsert":
        for (const packet of operation.packets) {
          // 先校验id是否合法
          assertValidUpsertId(packet.id);
          // document 由前端权威维护，忽略业务侧对其的 upsert。
          if (packet.id === "document") {
            continue;
          }
          const incoming = clonePacket(packet);
          const index = working.findIndex((existing) => existing.id === incoming.id);
          // 如果id已存在，则替换
          if (index >= 0) {
            working[index] = incoming;
          } else {
            // 如果id不存在，则添加
            working.push(incoming);
          }
        }
        break;
      case "delete":
        // 删除列出的实体 id，始终保留 document packet。
        working = working.filter(
          (packet) => packet.id === "document" || !operation.ids.includes(packet.id),
        );
        break;
      case "style":
        applyStyleOperation(working, operation.id, operation.patch);
        break;
      case "camera":
        // 相机 ops 不参与文档归约；应由 SceneManager 路由到 CameraController。
        throw new Error(
          "相机 SceneOp 尚未支持（unsupported）：请等待相机控制器接入后再执行。",
        );
    }
  }

  return working;
}
