import type {
  CzmlPacket,
  EntitySummary,
  SceneSummary,
} from "../contracts/chat";

function readCartographicDegrees(packet: CzmlPacket): {
  lon?: number;
  lat?: number;
  alt?: number;
} {
  const position = packet.position;
  if (!position || typeof position !== "object") {
    return {};
  }

  const cartographicDegrees = (position as Record<string, unknown>)
    .cartographicDegrees;
  if (!Array.isArray(cartographicDegrees) || cartographicDegrees.length < 3) {
    return {};
  }

  const [lon, lat, alt] = cartographicDegrees;
  if (
    typeof lon !== "number" ||
    typeof lat !== "number" ||
    typeof alt !== "number"
  ) {
    return {};
  }

  return { lon, lat, alt };
}

function readOrbitHint(packet: CzmlPacket): string | undefined {
  const properties = packet.properties;
  if (properties && typeof properties === "object") {
    const orbitHint = (properties as Record<string, unknown>).orbitHint;
    if (orbitHint && typeof orbitHint === "object") {
      const hintString = (orbitHint as Record<string, unknown>).string;
      if (typeof hintString === "string") {
        return hintString;
      }
    }
  }

  return typeof packet.name === "string" ? packet.name : undefined;
}

function classifyEntity(packet: CzmlPacket): EntitySummary {
  const type =
    "path" in packet ? "satellite" : "point" in packet ? "facility" : "other";

  const summary: EntitySummary = {
    id: packet.id,
    type,
  };

  if (typeof packet.name === "string") {
    summary.name = packet.name;
  }

  if (type === "facility") {
    Object.assign(summary, readCartographicDegrees(packet));
  }

  if (type === "satellite") {
    summary.orbitHint = readOrbitHint(packet);
  }

  return summary;
}

export function buildSceneSummary(document: CzmlPacket[]): SceneSummary {
  const documentPacket = document.find((packet) => packet.id === "document");
  const clock =
    documentPacket &&
    documentPacket.clock &&
    typeof documentPacket.clock === "object"
      ? (documentPacket.clock as Record<string, unknown>)
      : undefined;

  return {
    documentClock: clock
      ? {
          interval:
            typeof clock.interval === "string" ? clock.interval : undefined,
          currentTime:
            typeof clock.currentTime === "string"
              ? clock.currentTime
              : undefined,
        }
      : undefined,
    entities: document
      .filter((packet) => packet.id !== "document")
      .map(classifyEntity),
  };
}

export function pickRelevantPackets(
  document: CzmlPacket[],
  ids: string[],
): CzmlPacket[] {
  const idSet = new Set(ids);
  return document
    .filter((packet) => idSet.has(packet.id))
    .map((packet) => structuredClone(packet));
}

function matchesEntityText(text: string, entity: EntitySummary): boolean {
  const haystack = text.toLowerCase();
  if (haystack.includes(entity.id.toLowerCase())) {
    return true;
  }
  if (entity.name && haystack.includes(entity.name.toLowerCase())) {
    return true;
  }
  return false;
}

export function inferRelevantEntityIds(
  text: string,
  summary: SceneSummary,
  selectedIds: string[],
): string[] {
  const seen = new Set<string>();
  const result: string[] = [];

  for (const id of selectedIds) {
    if (!seen.has(id)) {
      seen.add(id);
      result.push(id);
    }
  }

  for (const entity of summary.entities) {
    if (seen.has(entity.id)) {
      continue;
    }
    if (matchesEntityText(text, entity)) {
      seen.add(entity.id);
      result.push(entity.id);
    }
  }

  return result;
}
