import { readFileSync } from "node:fs";

const DATE_HEADING = /^##\s+(\d{4})-(\d{2})-(\d{2})(?!\d)/gm;

/**
 * 从 CHANGES.md 文本中取最新日期条目，返回 `YYYYMMDD`。
 * 取所有 `## YYYY-MM-DD` 标题中的最大值，即使条目顺序写错也不会显示旧日期。
 */
export function parseLatestChangeDate(markdown: string): string {
  let latest: string | null = null;
  for (const [, year, month, day] of markdown.matchAll(DATE_HEADING)) {
    const date = new Date(`${year}-${month}-${day}T00:00:00Z`);
    if (
      Number.isNaN(date.getTime()) ||
      date.getUTCMonth() + 1 !== Number(month) ||
      date.getUTCDate() !== Number(day)
    ) {
      throw new Error(`CHANGES.md 中存在非法日期标题：${year}-${month}-${day}`);
    }
    const compact = `${year}${month}${day}`;
    if (latest === null || compact > latest) {
      latest = compact;
    }
  }
  if (latest === null) {
    throw new Error("CHANGES.md 中未找到形如 `## YYYY-MM-DD` 的日期条目");
  }
  return latest;
}

export function readLatestChangeDate(changesPath: string): string {
  return parseLatestChangeDate(readFileSync(changesPath, "utf8"));
}
