import { memo } from "react";
import Markdown, { type Components } from "react-markdown";
import remarkGfm from "remark-gfm";

export type MessageContentProps = {
  role: "user" | "assistant";
  text: string;
};

const remarkPlugins = [remarkGfm];

const markdownComponents: Components = {
  a: ({ node: _node, ...props }) => (
    <a {...props} target="_blank" rel="noopener noreferrer" />
  ),
  // 窄聊天面板内宽表格需横向滚动，而不是撑破气泡。
  table: ({ node: _node, ...props }) => (
    <div className="message-table-scroll">
      <table {...props} />
    </div>
  ),
};

/**
 * 聊天消息正文：用户消息保持纯文本；助手消息按 GFM 渲染 Markdown。
 * 不启用 rehype-raw，原始 HTML 由 react-markdown 转为纯文本显示，不会被执行；
 * 危险协议链接（如 `javascript:`）由默认 urlTransform 清空。
 * 流式输出中的半截 Markdown（未闭合的代码块、加粗、表格）也能稳定解析。
 */
export const MessageContent = memo(function MessageContent({
  role,
  text,
}: MessageContentProps) {
  if (role === "user") {
    return <p className="message-bubble">{text}</p>;
  }
  return (
    <div className="message-bubble message-markdown">
      <Markdown remarkPlugins={remarkPlugins} components={markdownComponents}>
        {text}
      </Markdown>
    </div>
  );
});
