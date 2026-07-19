import type { ChatRequest, ChatResponse } from "../contracts/chat";
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
  // 开发环境通常配置 VITE_API_BASE_URL=http://localhost:5088；未配置则走同源
  const response = await fetch(
    `${import.meta.env.VITE_API_BASE_URL ?? ""}/api/chat`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
      signal,
    },
  );

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
