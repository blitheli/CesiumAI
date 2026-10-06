import { useState, type FormEvent, type KeyboardEvent } from "react";

export type UiMessage = {
  id: string;
  role: "user" | "assistant";
  text: string;
  /** 助手回复仍在流式输出中。 */
  streaming?: boolean;
};

export type ChatPanelProps = {
  messages: UiMessage[];
  loading: boolean;
  /** 流式进度提示（如正在调用的工具）；为空时显示通用“正在处理…”。 */
  activity?: string | null;
  error: string | null;
  onSend: (text: string) => void | Promise<void>;
};

export function ChatPanel({
  messages,
  loading,
  activity = null,
  error,
  onSend,
}: ChatPanelProps) {
  const [draft, setDraft] = useState("");

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const text = draft.trim();
    if (!text || loading) {
      return;
    }
    setDraft("");
    void onSend(text);
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (
      event.key !== "Enter" ||
      event.shiftKey ||
      event.nativeEvent.isComposing ||
      event.nativeEvent.keyCode === 229
    ) {
      return;
    }
    event.preventDefault();
    event.currentTarget.form?.requestSubmit();
  };

  return (
    <aside className="chat-panel" aria-label="场景助手">
      <header className="chat-header">
        <h1>CesiumAI</h1>
        <p>用自然语言探索和编辑场景</p>
      </header>

      <div className="message-list" aria-live="polite">
        {messages.length === 0 ? (
          <p className="empty-message">选择实体，或直接描述你想看到的变化。</p>
        ) : null}
        {messages.map((message) => (
          <div
            className={`message message-${message.role}${message.streaming ? " message-streaming" : ""}`}
            data-role={message.role}
            data-streaming={message.streaming ? "true" : undefined}
            aria-busy={message.streaming ? true : undefined}
            key={message.id}
          >
            <span className="message-label">
              {message.role === "user" ? "你" : "助手"}
            </span>
            <p>{message.text}</p>
          </div>
        ))}
      </div>

      <div className="chat-feedback">
        {loading ? <p role="status">{activity ?? "正在处理…"}</p> : null}
        {error ? <p role="alert">{error}</p> : null}
      </div>

      <form className="chat-composer" onSubmit={submit}>
        <label htmlFor="chat-message">消息</label>
        <textarea
          id="chat-message"
          value={draft}
          disabled={loading}
          placeholder="例如：把 sanya 高度改为 50"
          rows={3}
          onChange={(event) => setDraft(event.currentTarget.value)}
          onKeyDown={handleKeyDown}
        />
        <button type="submit" disabled={loading}>
          {loading ? "发送中…" : "发送"}
        </button>
      </form>
    </aside>
  );
}
