/**
 * 场景摘要与相关实体选取：把全量 CZML 压成轻量 `SceneSummary`，
 * 并推断本轮聊天该附带哪些完整 packet。
 *
 * 目的：`ChatRequest` 不塞全量场景——只发摘要 + `relevantPackets`。
 * - `buildSceneSummary`：文档 → 摘要（设施经纬高、卫星 orbitHint 等）
 * - `inferRelevantEntityIds`：用户文本 + 选中 → 相关 id
 * - `pickRelevantPackets`：按 id 从文档取出完整 packet 深拷贝
 *
 * 详见 Docs/前端说明.md §4.8。
 */
import type {
  CzmlPacket,
  EntitySummary,
  SceneSummary,
} from "../contracts/chat";

/**
 * 从设施类 packet 的 `position.cartographicDegrees` 读取 [lon, lat, alt]。
 * 格式不符或缺字段时返回空对象（摘要里可不带坐标）。
 */
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

/**
 * 读取卫星轨道提示：优先 `properties.orbitHint.string`（CZML property 包装），
 * 否则回退到 `packet.name`。
 */
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

/**
 * 将单个业务 packet 压成 `EntitySummary`。
 * 启发式分类：有 `path` → satellite；有 `point` → facility；否则 other。
 */
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

/**
 * 从内存 CZML 文档生成发给后端的轻量场景摘要。
 * 包含 document 时钟窗口（若有）以及除 document 外全部实体的摘要列表。
 */
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

/**
 * 按实体 id 从文档取出完整 packet（深拷贝），供 `ChatRequest.relevantPackets` 使用。
 * 未命中的 id 静默跳过；返回顺序跟随文档中出现顺序。
 */
export function pickRelevantPackets(
  document: CzmlPacket[],
  ids: string[],
): CzmlPacket[] {
  const idSet = new Set(ids);
  return document
    .filter((packet) => idSet.has(packet.id))
    .map((packet) => structuredClone(packet));
}

/** 用户文本是否提及该实体的 id 或 name（大小写不敏感子串匹配）。 */
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

/**
 * 推断本轮相关实体 id：当前选中优先，再按用户文本匹配摘要中的 id/name。
 * 结果去重且保持「先选中、后文本命中」的顺序。
 */
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
