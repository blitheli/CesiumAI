import type { CzmlPacket, SceneOp } from "../contracts/chat";

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
    }
  }

  return working;
}
