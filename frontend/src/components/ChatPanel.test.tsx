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

it("renders user and assistant messages as plain text", () => {
  const { container } = renderPanel({ messages });

  expect(screen.getByText("移动三亚")).toBeInTheDocument();
  expect(
    screen.getByText("已移动 <img src=x onerror=alert(1)>"),
  ).toBeInTheDocument();
  expect(container.querySelector("img")).not.toBeInTheDocument();
});

it("announces errors", () => {
  renderPanel({ error: "请求失败" });

  expect(screen.getByRole("alert")).toHaveTextContent("请求失败");
});
