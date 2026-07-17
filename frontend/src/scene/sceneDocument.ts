import type { CzmlPacket, SceneOp } from "../contracts/chat";
import { applyStylePatch } from "./sceneStyle";

function clonePacket(packet: CzmlPacket): CzmlPacket {
  return structuredClone(packet);
}

function cloneDocument(document: CzmlPacket[]): CzmlPacket[] {
  return document.map(clonePacket);
}

function assertValidUpsertId(id: unknown): asserts id is string {
  if (typeof id !== "string" || id.trim() === "") {
    throw new Error("Upsert packet id must be a non-empty string");
  }
}

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

export function reduceSceneDocument(
  current: CzmlPacket[],
  operations: SceneOp[],
  emptyDocument: CzmlPacket[],
): CzmlPacket[] {
  let working = cloneDocument(current);

  for (const operation of operations) {
    switch (operation.op) {
      case "clear":
        working = cloneDocument(emptyDocument);
        break;
      case "upsert":
        for (const packet of operation.packets) {
          assertValidUpsertId(packet.id);
          if (packet.id === "document") {
            continue;
          }
          const incoming = clonePacket(packet);
          const index = working.findIndex((existing) => existing.id === incoming.id);
          if (index >= 0) {
            working[index] = incoming;
          } else {
            working.push(incoming);
          }
        }
        break;
      case "delete":
        working = working.filter(
          (packet) => packet.id === "document" || !operation.ids.includes(packet.id),
        );
        break;
      case "style":
        applyStyleOperation(working, operation.id, operation.patch);
        break;
      case "camera":
        throw new Error(
          "相机 SceneOp 尚未支持（unsupported）：请等待相机控制器接入后再执行。",
        );
    }
  }

  return working;
}
