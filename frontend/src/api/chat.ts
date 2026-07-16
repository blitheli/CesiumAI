import type { ChatRequest, ChatResponse } from "../contracts/chat";

function isChatResponse(value: unknown): value is ChatResponse {
  if (!value || typeof value !== "object") {
    return false;
  }

  const result = value as Partial<ChatResponse>;
  return (
    typeof result.sessionId === "string" &&
    typeof result.message === "string" &&
    Array.isArray(result.sceneOps)
  );
}

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
    // Fall back to the HTTP status below.
  }
  return `Chat request failed (${response.status})`;
}

export async function postChat(
  request: ChatRequest,
  signal: AbortSignal,
): Promise<ChatResponse> {
  const response = await fetch(
    `${import.meta.env.VITE_API_BASE_URL ?? ""}/api/chat`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
      signal,
    },
  );

  if (!response.ok) {
    throw new Error(await readErrorDetail(response));
  }

  const body: unknown = await response.json();
  if (!isChatResponse(body)) {
    throw new Error("Invalid chat response");
  }
  return body;
}
