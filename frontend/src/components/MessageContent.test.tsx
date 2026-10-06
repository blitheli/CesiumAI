import { render, screen } from "@testing-library/react";
import { vi } from "vitest";
import { MessageContent } from "./MessageContent";

function renderAssistant(text: string) {
  return render(<MessageContent role="assistant" text={text} />);
}

it("把 GFM 表格渲染为可横向滚动的 table", () => {
  const { container } = renderAssistant(
    "轨道参数：\n\n| 参数 | 值 |\n| --- | --- |\n| 高度 | 900 km |\n| 倾角 | 99.0° |\n",
  );

  const table = screen.getByRole("table");
  expect(table.parentElement).toHaveClass("message-table-scroll");
  expect(screen.getByRole("columnheader", { name: "参数" })).toBeInTheDocument();
  expect(screen.getByRole("cell", { name: "900 km" })).toBeInTheDocument();
  expect(container.textContent).not.toContain("| --- |");
});

it("渲染加粗、行内代码、列表与代码块", () => {
  const { container } = renderAssistant(
    [
      "已添加 **国际空间站**，实体 ID 为 `iss`。",
      "",
      "- 第一项",
      "- 第二项",
      "",
      "```json",
      '{ "op": "clear" }',
      "```",
    ].join("\n"),
  );

  expect(screen.getByText("国际空间站").tagName).toBe("STRONG");
  expect(screen.getByText("iss").tagName).toBe("CODE");
  expect(screen.getAllByRole("listitem").map((item) => item.textContent)).toEqual([
    "第一项",
    "第二项",
  ]);
  const block = container.querySelector("pre > code");
  expect(block).toHaveTextContent('{ "op": "clear" }');
  expect(container.textContent).not.toContain("**");
});

it("链接在新窗口打开并带 rel，危险协议被清除", () => {
  renderAssistant("见 [文档](https://cesium.com/docs) 与 [坏链接](javascript:alert(1))");

  const safe = screen.getByRole("link", { name: "文档" });
  expect(safe).toHaveAttribute("href", "https://cesium.com/docs");
  expect(safe).toHaveAttribute("target", "_blank");
  expect(safe).toHaveAttribute("rel", "noopener noreferrer");
  expect(screen.getByText("坏链接").closest("a")?.getAttribute("href") ?? "").not.toMatch(
    /javascript:/i,
  );
});

it("原始 HTML 作为纯文本显示而不会被执行", () => {
  const alertSpy = vi.spyOn(window, "alert").mockImplementation(() => {});
  const { container } = renderAssistant(
    '已移动 <img src=x onerror=alert(1)>\n\n<script>alert(2)</script>\n\n<b onclick="alert(3)">粗</b>',
  );

  expect(container.querySelector("img, script, b")).not.toBeInTheDocument();
  expect(container.textContent).toContain("<img src=x onerror=alert(1)>");
  expect(container.textContent).toContain("<script>alert(2)</script>");
  expect(alertSpy).not.toHaveBeenCalled();
  alertSpy.mockRestore();
});

it.each([
  ["未闭合加粗", "正在设置 **高度", "正在设置 **高度"],
  ["未闭合代码块", "结果：\n\n```json\n{ \"op\": ", '{ "op":'],
  ["半截表格", "| 参数 | 值 |\n| --- |", "| 参数 | 值 |"],
  ["只有表头分隔的表格", "| 参数 | 值 |\n| --- | --- |", "参数"],
  ["空文本", "", ""],
])("流式半截 Markdown（%s）可正常渲染", (_name, text, expected) => {
  const { container } = renderAssistant(text);

  const body = container.querySelector(".message-markdown");
  expect(body).toBeInTheDocument();
  expect(body?.textContent).toContain(expected);
});

it("用户消息保持纯文本，不解析 Markdown", () => {
  const { container } = render(
    <MessageContent role="user" text={"把 **sanya** 改成 `50`\n| a | b |"} />,
  );

  const bubble = container.querySelector("p.message-bubble");
  expect(bubble).toHaveTextContent("把 **sanya** 改成 `50` | a | b |");
  expect(container.querySelector("strong, code, table")).not.toBeInTheDocument();
});
