import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import { ChatPanel, type UiMessage } from "./ChatPanel";

const messages: UiMessage[] = [
  { id: "user-1", role: "user", text: "移动三亚" },
  {
    id: "assistant-1",
    role: "assistant",
    text: "已移动 <img src=x onerror=alert(1)>",
  },
];

function renderPanel(
  overrides: Partial<React.ComponentProps<typeof ChatPanel>> = {},
) {
  const props: React.ComponentProps<typeof ChatPanel> = {
    messages: [],
    loading: false,
    error: null,
    onSend: vi.fn(),
    ...overrides,
  };
  return { ...render(<ChatPanel {...props} />), props };
}

it("副标题末尾显示构建注入的最新更新日期", () => {
  renderPanel();

  expect(__APP_LAST_UPDATED__).toMatch(/^\d{8}$/);
  expect(
    screen.getByText(`用自然语言探索和编辑场景-${__APP_LAST_UPDATED__}`),
  ).toBeInTheDocument();
});

it("submits a non-empty message", async () => {
  const user = userEvent.setup();
  const onSend = vi.fn();
  renderPanel({ onSend });

  await user.type(screen.getByLabelText("消息"), "把 sanya 高度改为 50");
  await user.click(screen.getByRole("button", { name: "发送" }));

  expect(onSend).toHaveBeenCalledOnce();
  expect(onSend).toHaveBeenCalledWith("把 sanya 高度改为 50");
});

it("disables the composer while loading", () => {
  renderPanel({ loading: true });

  expect(screen.getByLabelText("消息")).toBeDisabled();
  expect(screen.getByRole("button", { name: "发送中…" })).toBeDisabled();
  expect(screen.getByRole("status")).toHaveTextContent("正在处理");
});

it("ignores blank messages", async () => {
  const user = userEvent.setup();
  const onSend = vi.fn();
  renderPanel({ onSend });

  await user.type(screen.getByLabelText("消息"), "   ");
  await user.click(screen.getByRole("button", { name: "发送" }));

  expect(onSend).not.toHaveBeenCalled();
});

it("submits with Enter", async () => {
  const user = userEvent.setup();
  const onSend = vi.fn();
  renderPanel({ onSend });

  await user.type(screen.getByLabelText("消息"), "更新三亚{Enter}");

  expect(onSend).toHaveBeenCalledWith("更新三亚");
});

it("does not submit composing Enter and submits after composition ends", () => {
  const onSend = vi.fn();
  renderPanel({ onSend });
  const textarea = screen.getByLabelText("消息");
  fireEvent.change(textarea, { target: { value: "三亚" } });

  fireEvent.compositionStart(textarea);
  fireEvent.keyDown(textarea, {
    key: "Enter",
    code: "Enter",
    isComposing: true,
  });

  expect(onSend).not.toHaveBeenCalled();
  expect(textarea).toHaveValue("三亚");

  fireEvent.compositionEnd(textarea);
  fireEvent.keyDown(textarea, {
    key: "Enter",
    code: "Enter",
    isComposing: false,
  });

  expect(onSend).toHaveBeenCalledOnce();
  expect(onSend).toHaveBeenCalledWith("三亚");
  expect(textarea).toHaveValue("");
});

it("does not submit legacy IME Enter with keyCode 229", () => {
  const onSend = vi.fn();
  renderPanel({ onSend });
  const textarea = screen.getByLabelText("消息");
  fireEvent.change(textarea, { target: { value: "北京" } });

  fireEvent.keyDown(textarea, {
    key: "Enter",
    code: "Enter",
    isComposing: false,
    keyCode: 229,
  });

  expect(onSend).not.toHaveBeenCalled();
  expect(textarea).toHaveValue("北京");
});

it("inserts a newline with Shift+Enter", async () => {
  const user = userEvent.setup();
  const onSend = vi.fn();
  renderPanel({ onSend });
  const textarea = screen.getByLabelText("消息");

  await user.type(textarea, "第一行{Shift>}{Enter}{/Shift}第二行");

  expect(textarea).toHaveValue("第一行\n第二行");
  expect(onSend).not.toHaveBeenCalled();
});

it("renders user messages as plain text and assistant HTML as escaped text", () => {
  const { container } = renderPanel({ messages });

  expect(screen.getByText("移动三亚")).toBeInTheDocument();
  expect(
    screen.getByText("已移动 <img src=x onerror=alert(1)>"),
  ).toBeInTheDocument();
  expect(container.querySelector("img")).not.toBeInTheDocument();
});

it("renders assistant Markdown but keeps user Markdown literal", () => {
  const { container } = renderPanel({
    messages: [
      { id: "user-1", role: "user", text: "**原样**" },
      { id: "assistant-1", role: "assistant", text: "已设置 **高度** 为 `50`" },
    ],
  });

  expect(screen.getByText("**原样**")).toBeInTheDocument();
  const assistant = container.querySelector('[data-role="assistant"]');
  expect(assistant?.querySelector("strong")).toHaveTextContent("高度");
  expect(assistant?.querySelector("code")).toHaveTextContent("50");
});

it("keeps the streaming marker while rendering half-finished assistant Markdown", () => {
  const { container } = renderPanel({
    loading: true,
    messages: [
      {
        id: "assistant-1",
        role: "assistant",
        text: "| 参数 | 值 |\n| --- | --- |\n| 高度 | **9",
        streaming: true,
      },
    ],
  });

  const message = container.querySelector('[data-role="assistant"]');
  expect(message).toHaveAttribute("data-streaming", "true");
  expect(message).toHaveClass("message-streaming");
  expect(message?.querySelector(".message-markdown table")).toBeInTheDocument();
});

it("announces errors", () => {
  renderPanel({ error: "请求失败" });

  expect(screen.getByRole("alert")).toHaveTextContent("请求失败");
});

it("shows streaming activity in the status and marks the streaming message busy", () => {
  renderPanel({
    loading: true,
    activity: "正在调用工具 ClearScene…",
    messages: [
      { id: "assistant-1", role: "assistant", text: "正在清", streaming: true },
    ],
  });

  expect(screen.getByRole("status")).toHaveTextContent("正在调用工具 ClearScene…");
  const message = screen.getByText("正在清").closest('[data-role="assistant"]');
  expect(message).toHaveAttribute("data-streaming", "true");
  expect(message).toHaveAttribute("aria-busy", "true");
});

it("falls back to the generic status without activity and drops busy state when done", () => {
  renderPanel({
    loading: true,
    messages: [{ id: "assistant-1", role: "assistant", text: "完成" }],
  });

  expect(screen.getByRole("status")).toHaveTextContent("正在处理…");
  const message = screen.getByText("完成").closest('[data-role="assistant"]');
  expect(message).not.toHaveAttribute("data-streaming");
  expect(message).not.toHaveAttribute("aria-busy");
});
