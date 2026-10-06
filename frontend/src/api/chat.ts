import type {
  ChatRequest,
  ChatResponse,
  ChatStreamEvent,
} from "../contracts/chat";
import { isSceneOpArray } from "./sceneOpsRuntime";

/**
 * 判断未知 JSON 是否符合 ChatResponse 契约。
 * 用于在 response.json() 之后做运行时校验，避免畸形 sceneOps 进入场景层。
 */
function isChatResponse(value: unknown): value is ChatResponse {
  // 必须是非 null 的普通对象
  if (!value || typeof value !== "object") {
    return false;
  }

  const result = value as Partial<ChatResponse>;
  return (
    // 会话 ID：后续请求回传以保持同一 Agent 会话
    typeof result.sessionId === "string" &&
    // 助手自然语言回复
    typeof result.message === "string" &&
    // 场景操作数组：须通过 op 白名单校验（clear/upsert/delete/camera/style）
    isSceneOpArray(result.sceneOps)
  );
}

/**
 * 从失败的 HTTP 响应中提取可读错误信息。
 * 优先使用后端 Problem Details / 自定义体中的 detail 字段；
 * 若 body 不是 JSON 或没有 detail，则回退为带状态码的通用文案。
 */
async function readErrorDetail(response: Response): Promise<string> {
  try {
    const body: unknown = await response.json();
    if (body && typeof body === "object") {
      const detail = (body as { detail?: unknown }).detail;
      if (typeof detail === "string") {
        return detail;
      }
    }
  } catch {
    // JSON 解析失败时忽略，下面用 HTTP 状态码兜底
  }
  return `Chat request failed (${response.status})`;
}

function chatUrl(path: string): string {
  // 开发环境通常配置 VITE_API_BASE_URL=http://localhost:5088；未配置则走同源
  return `${import.meta.env.VITE_API_BASE_URL ?? ""}${path}`;
}

/**
 * 向后端发起一轮聊天（整包请求/响应，非流式）。
 *
 * @param request 用户消息、可选 sessionId、场景摘要与相关 CZML packets
 * @param signal  用于取消未完成的 fetch（例如组件卸载或用户中止）
 * @returns 校验通过的 ChatResponse（含 sessionId、message、sceneOps）
 * @throws 网络/HTTP 失败，或响应结构不合法时抛出 Error
 */
export async function postChat(
  request: ChatRequest,
  signal: AbortSignal,
): Promise<ChatResponse> {
  const response = await fetch(chatUrl("/api/chat"), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
    signal,
  });

  // 4xx/5xx：尽量展示后端 detail，便于排查 Agent/超时等问题
  if (!response.ok) {
    throw new Error(await readErrorDetail(response));
  }

  // 成功体先当 unknown，再做契约校验，避免信任远端随意 JSON
  const body: unknown = await response.json();
  if (!isChatResponse(body)) {
    throw new Error("Invalid chat response: malformed sceneOps");
  }
  return body;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}

/** 把一个 SSE 帧（event 名 + data JSON）校验为 ChatStreamEvent；畸形返回 null。 */
function toStreamEvent(name: string, data: unknown): ChatStreamEvent | null {
  if (!isRecord(data)) {
    return null;
  }

  switch (name) {
    case "session":
      return typeof data.sessionId === "string"
        ? { type: "session", sessionId: data.sessionId }
        : null;
    case "delta":
      return typeof data.text === "string"
        ? { type: "delta", text: data.text }
        : null;
    case "tool_call":
      return typeof data.callId === "string" && typeof data.name === "string"
        ? { type: "tool_call", callId: data.callId, name: data.name }
        : null;
    case "tool_result":
      return typeof data.callId === "string" &&
        typeof data.succeeded === "boolean"
        ? {
            type: "tool_result",
            callId: data.callId,
            succeeded: data.succeeded,
          }
        : null;
    case "done":
      return isChatResponse(data)
        ? {
            type: "done",
            sessionId: data.sessionId,
            message: data.message,
            sceneOps: data.sceneOps,
          }
        : null;
    case "error":
      return typeof data.error === "string" && typeof data.detail === "string"
        ? { type: "error", error: data.error, detail: data.detail }
        : null;
    default:
      return null;
  }
}

const KNOWN_STREAM_EVENTS: ReadonlySet<string> = new Set<
  ChatStreamEvent["type"]
>(["session", "delta", "tool_call", "tool_result", "done", "error"]);

type SseFrame = { event: string; data: string };

/** 解析单个 SSE 帧文本（不含结尾空行）；无 data 的帧（如注释/心跳）返回 null。 */
function parseSseFrame(frame: string): SseFrame | null {
  let event = "message";
  const dataLines: string[] = [];

  for (const line of frame.split("\n")) {
    if (line === "" || line.startsWith(":")) {
      continue;
    }
    const separator = line.indexOf(":");
    const field = separator === -1 ? line : line.slice(0, separator);
    let value = separator === -1 ? "" : line.slice(separator + 1);
    if (value.startsWith(" ")) {
      value = value.slice(1);
    }
    if (field === "event") {
      event = value;
    } else if (field === "data") {
      dataLines.push(value);
    }
  }

  return dataLines.length === 0 ? null : { event, data: dataLines.join("\n") };
}

/** 逐帧读取 SSE 响应体；兼容 CRLF / CR 换行与跨 chunk 的帧边界。 */
async function* readSseFrames(
  body: ReadableStream<Uint8Array>,
): AsyncGenerator<SseFrame> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  // chunk 末尾的单个 \r 可能与下个 chunk 开头的 \n 组成 CRLF，暂存到下一轮再归一化。
  let pendingCarriageReturn = false;
  const normalize = (text: string, final: boolean) => {
    let combined = (pendingCarriageReturn ? "\r" : "") + text;
    pendingCarriageReturn = !final && combined.endsWith("\r");
    if (pendingCarriageReturn) {
      combined = combined.slice(0, -1);
    }
    return combined.replace(/\r\n?/g, "\n");
  };

  try {
    for (;;) {
      const { value, done } = await reader.read();
      if (done) {
        buffer += normalize(decoder.decode(), true);
        break;
      }
      // stream: true 保证跨 chunk 的多字节 UTF-8 字符（中文）不被截断。
      buffer += normalize(decoder.decode(value, { stream: true }), false);

      let boundary = buffer.indexOf("\n\n");
      while (boundary !== -1) {
        const frame = parseSseFrame(buffer.slice(0, boundary));
        buffer = buffer.slice(boundary + 2);
        if (frame) {
          yield frame;
        }
        boundary = buffer.indexOf("\n\n");
      }
    }
    // 按 SSE 规范，流结束时未以空行收尾的残帧直接丢弃。
  } finally {
    reader.releaseLock();
  }
}

/** 流式进度回调；可返回 Promise，读取下一事件前会等待它完成。 */
export type ChatStreamListener = (
  event: ChatStreamEvent,
) => void | Promise<void>;

/**
 * 以 SSE 流式发起一轮聊天：`POST /api/chat/stream`。
 *
 * 每个校验通过的事件依次交给 `onEvent`（用于逐步渲染文本与工具进度）；
 * 收到 `done` 时返回其 ChatResponse 部分。
 *
 * @throws HTTP 失败、`error` 事件、畸形事件（含畸形 sceneOps），或流在 `done` 之前结束
 */
export async function streamChat(
  request: ChatRequest,
  signal: AbortSignal,
  onEvent?: ChatStreamListener,
): Promise<ChatResponse> {
  const response = await fetch(chatUrl("/api/chat/stream"), {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Accept: "text/event-stream",
    },
    body: JSON.stringify(request),
    signal,
  });

  if (!response.ok) {
    throw new Error(await readErrorDetail(response));
  }
  if (!response.body) {
    throw new Error("Invalid chat stream: empty body");
  }

  for await (const frame of readSseFrames(response.body)) {
    // 向前兼容：忽略后端将来新增、本客户端尚不认识的事件类型。
    if (!KNOWN_STREAM_EVENTS.has(frame.event)) {
      continue;
    }

    let data: unknown;
    try {
      data = JSON.parse(frame.data);
    } catch {
      throw new Error(`Invalid chat stream: malformed ${frame.event} event`);
    }

    const event = toStreamEvent(frame.event, data);
    if (!event) {
      throw new Error(
        frame.event === "done"
          ? "Invalid chat response: malformed sceneOps"
          : `Invalid chat stream: malformed ${frame.event} event`,
      );
    }

    if (event.type === "error") {
      throw new Error(event.detail || event.error);
    }

    await onEvent?.(event);

    if (event.type === "done") {
      return {
        sessionId: event.sessionId,
        message: event.message,
        sceneOps: event.sceneOps,
      };
    }
  }

  throw new Error("Chat stream ended before completion");
}
