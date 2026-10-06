// @vitest-environment node
import { fileURLToPath } from "node:url";
import { expect, it } from "vitest";
import { parseLatestChangeDate, readLatestChangeDate } from "./changelogDate.ts";

it("返回最新日期条目的 YYYYMMDD", () => {
  const markdown = [
    "# 更新记录",
    "",
    "## 2026-10-06",
    "",
    "- 新功能",
    "",
    "## 2026-07-17",
    "",
    "- 旧功能",
  ].join("\n");

  expect(parseLatestChangeDate(markdown)).toBe("20261006");
});

it("条目顺序写错时仍取最大日期", () => {
  const markdown = "## 2026-07-17\n\n- 旧\n\n## 2026-10-06\n\n- 新\n";

  expect(parseLatestChangeDate(markdown)).toBe("20261006");
});

it("忽略非二级标题与正文中的日期", () => {
  const markdown = [
    "# 2099-01-01",
    "### 2098-01-01",
    "- 参见 2097-01-01 的说明",
    "## 2026-10-06（含 PR 链接）",
  ].join("\n");

  expect(parseLatestChangeDate(markdown)).toBe("20261006");
});

it("没有日期条目时抛错", () => {
  expect(() => parseLatestChangeDate("# 更新记录\n\n暂无")).toThrow(
    /未找到/,
  );
});

it("非法日期时抛错", () => {
  expect(() => parseLatestChangeDate("## 2026-02-30\n")).toThrow(/非法日期/);
});

it("仓库根目录 CHANGES.md 可解析出日期", () => {
  const changesPath = fileURLToPath(new URL("../../CHANGES.md", import.meta.url));

  expect(readLatestChangeDate(changesPath)).toMatch(/^\d{8}$/);
});
