import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ChatRequest } from "../contracts/chat";
import { postChat, streamChat } from "./chat";

const request: ChatRequest = {
  message: "把 sanya 高度改为 50",
  sessionId: "session-1",
  sceneSummary: {
    entities: [{ id: "sanya", name: "Sanya", type: "facility" }],
  },
  relevantPackets: [{ id: "sanya", name: "Sanya" }],
};

describe("postChat", () => {
  beforeEach(() => {
    vi.stubEnv("VITE_API_BASE_URL", "https://api.example");
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.unstubAllGlobals();
  });

  it("posts every request field as JSON and passes through the abort signal", async () => {
    const response = {
      sessionId: "session-2",
      message: "已更新",
      sceneOps: [{ op: "delete", ids: ["old"] }],
    };
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(response)));
    vi.stubGlobal("fetch", fetchMock);
    const controller = new AbortController();

    await expect(postChat(request, controller.signal)).resolves.toEqual(response);

    expect(fetchMock).toHaveBeenCalledWith("https://api.example/api/chat", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
      signal: controller.signal,
    });
  });

  it("uses a relative API URL when no base URL is configured", async () => {
    vi.stubEnv("VITE_API_BASE_URL", "");
    const fetchMock = vi.fn(async () =>
      new Response(
        JSON.stringify({ sessionId: "session-1", message: "ok", sceneOps: [] }),
      ),
    );
    vi.stubGlobal("fetch", fetchMock);

    await postChat(request, new AbortController().signal);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/chat",
      expect.objectContaining({ method: "POST" }),
    );
  });

  it("includes the server detail in non-success errors", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () =>
        new Response(JSON.stringify({ detail: "model unavailable" }), {
          status: 503,
        }),
      ),
    );

    await expect(
      postChat(request, new AbortController().signal),
    ).rejects.toThrow("model unavailable");
  });

  it.each([
    { message: "ok", sceneOps: [] },
    { sessionId: "session-1", sceneOps: [] },
    { sessionId: "session-1", message: "ok" },
  ])("rejects malformed success JSON: %j", async (body) => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(JSON.stringify(body))),
    );

    await expect(
      postChat(request, new AbortController().signal),
    ).rejects.toThrow("Invalid chat response");
  });

  it("accepts complete shapes for clear/upsert/delete/camera/style", async () => {
    const response = {
      sessionId: "session-2",
      message: "ok",
      sceneOps: [
        { op: "clear" },
        { op: "upsert", packets: [{ id: "iss" }] },
        { op: "delete", ids: ["old"] },
        { op: "camera", action: "focus", targetId: "iss" },
        { op: "style", id: "iss", patch: { path: { width: 5 } } },
      ],
    };
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(JSON.stringify(response))),
    );

    await expect(
      postChat(request, new AbortController().signal),
    ).resolves.toEqual(response);
  });

  it.each([
    { sceneOps: [{ op: "explode" }] },
    { sceneOps: [{ op: "upsert" }] },
    { sceneOps: [{ op: "upsert", packets: [] }] },
    { sceneOps: [{ op: "upsert", packets: [{ name: "no-id" }] }] },
    { sceneOps: [{ op: "delete" }] },
    { sceneOps: [{ op: "delete", ids: [] }] },
    { sceneOps: [{ op: "delete", ids: ["", "  "] }] },
    { sceneOps: [{ op: "camera", action: "warp" }] },
    { sceneOps: [{ op: "camera" }] },
    { sceneOps: [{ op: "camera", action: "focus" }] },
    { sceneOps: [{ op: "camera", action: "zoom", amount: 0 }] },
    { sceneOps: [{ op: "camera", action: "rotate" }] },
    { sceneOps: [{ op: "style", patch: { path: { width: 1 } } }] },
    { sceneOps: [{ op: "style", id: "", patch: { path: { width: 1 } } }] },
    { sceneOps: [{ op: "style", id: "iss" }] },
    { sceneOps: [{ op: "clear" }, { op: "unknown" }] },
    {
      sceneOps: [
        { op: "clear" },
        { op: "camera", action: "focus" },
      ],
    },
  ])(
    "rejects unknown or malformed sceneOps wholesale before apply: $sceneOps",
    async ({ sceneOps }) => {
      vi.stubGlobal(
        "fetch",
        vi.fn(async () =>
          new Response(
            JSON.stringify({
              sessionId: "session-1",
              message: "ok",
              sceneOps,
            }),
          ),
        ),
      );

      await expect(
        postChat(request, new AbortController().signal),
      ).rejects.toThrow(/Invalid chat response|sceneOps/);
    },
  );
});

const encoder = new TextEncoder();

/** 以给定 chunk 序列构造 SSE 响应体，可用于模拟任意分包边界。 */
function sseResponse(chunks: Array<string | Uint8Array>, init?: ResponseInit) {
  return new Response(
    new ReadableStream<Uint8Array>({
      start(controller) {
        for (const chunk of chunks) {
          controller.enqueue(
            typeof chunk === "string" ? encoder.encode(chunk) : chunk,
          );
        }
        controller.close();
      },
    }),
    { headers: { "Content-Type": "text/event-stream" }, ...init },
  );
}

function frame(event: string, data: unknown) {
  return `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
}

const doneResponse = {
  sessionId: "session-2",
  message: "已清空场景。",
  sceneOps: [{ op: "clear" }],
};

describe("streamChat", () => {
  beforeEach(() => {
    vi.stubEnv("VITE_API_BASE_URL", "https://api.example");
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.unstubAllGlobals();
  });

  it("posts to the stream endpoint, emits events in order, and resolves with done", async () => {
    const fetchMock = vi.fn(async () =>
      sseResponse([
        frame("session", { sessionId: "session-2" }),
        frame("delta", { text: "已清空" }),
        frame("tool_call", { callId: "c1", name: "ClearScene" }),
        frame("tool_result", { callId: "c1", succeeded: true }),
        frame("delta", { text: "场景。" }),
        frame("done", doneResponse),
      ]),
    );
    vi.stubGlobal("fetch", fetchMock);
    const controller = new AbortController();
    const events: unknown[] = [];

    await expect(
      streamChat(request, controller.signal, (event) => {
        events.push(event);
      }),
    ).resolves.toEqual(doneResponse);

    expect(fetchMock).toHaveBeenCalledWith("https://api.example/api/chat/stream", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Accept: "text/event-stream",
      },
      body: JSON.stringify(request),
      signal: controller.signal,
    });
    expect(events).toEqual([
      { type: "session", sessionId: "session-2" },
      { type: "delta", text: "已清空" },
      { type: "tool_call", callId: "c1", name: "ClearScene" },
      { type: "tool_result", callId: "c1", succeeded: true },
      { type: "delta", text: "场景。" },
      { type: "done", ...doneResponse },
    ]);
  });

  it("reassembles frames split across chunks, multi-byte characters, and CRLF", async () => {
    const body = encoder.encode(
      (frame("delta", { text: "中文增量" }) + frame("done", doneResponse)).replace(
        /\n/g,
        "\r\n",
      ),
    );
    // 逐字节切分：覆盖 UTF-8 多字节字符与 \r\n 被拆到两个 chunk 的情况。
    const chunks = Array.from(body, (byte) => Uint8Array.of(byte));
    vi.stubGlobal("fetch", vi.fn(async () => sseResponse(chunks)));
    const deltas: string[] = [];

    await expect(
      streamChat(request, new AbortController().signal, (event) => {
        if (event.type === "delta") {
          deltas.push(event.text);
        }
      }),
    ).resolves.toEqual(doneResponse);
    expect(deltas).toEqual(["中文增量"]);
  });

  it("ignores comments and unknown event types", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () =>
        sseResponse([
          ": keep-alive\n\n",
          frame("future_event", { anything: true }),
          frame("done", doneResponse),
        ]),
      ),
    );
    const listener = vi.fn();

    await expect(
      streamChat(request, new AbortController().signal, listener),
    ).resolves.toEqual(doneResponse);
    expect(listener).toHaveBeenCalledOnce();
  });

  it("waits for async listeners before reading the next event", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () =>
        sseResponse([frame("delta", { text: "a" }), frame("done", doneResponse)]),
      ),
    );
    const order: string[] = [];

    await streamChat(request, new AbortController().signal, async (event) => {
      order.push(`start:${event.type}`);
      await new Promise((resolve) => setTimeout(resolve, 5));
      order.push(`end:${event.type}`);
    });

    expect(order).toEqual(["start:delta", "end:delta", "start:done", "end:done"]);
  });

  it("throws the detail of an error event", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () =>
        sseResponse([
          frame("session", { sessionId: "s" }),
          frame("error", {
            error: "agent_timeout",
            detail: "Agent request exceeded 120 seconds.",
          }),
        ]),
      ),
    );

    await expect(
      streamChat(request, new AbortController().signal),
    ).rejects.toThrow("Agent request exceeded 120 seconds.");
  });

  it("includes the server detail in non-success HTTP errors", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () =>
        new Response(JSON.stringify({ detail: "bad request" }), { status: 400 }),
      ),
    );

    await expect(
      streamChat(request, new AbortController().signal),
    ).rejects.toThrow("bad request");
  });

  it("rejects a done event with malformed sceneOps", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () =>
        sseResponse([
          frame("done", {
            ...doneResponse,
            sceneOps: [{ op: "clear" }, { op: "camera", action: "focus" }],
          }),
        ]),
      ),
    );

    await expect(
      streamChat(request, new AbortController().signal),
    ).rejects.toThrow("Invalid chat response");
  });

  it.each([
    ["delta", { text: 1 }],
    ["tool_call", { callId: "c1" }],
    ["tool_result", { callId: "c1", succeeded: "yes" }],
    ["session", {}],
  ])("rejects malformed %s events", async (event, data) => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => sseResponse([frame(event, data), frame("done", doneResponse)])),
    );

    await expect(
      streamChat(request, new AbortController().signal),
    ).rejects.toThrow(`Invalid chat stream: malformed ${event} event`);
  });

  it("rejects non-JSON data", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => sseResponse(["event: delta\ndata: {oops\n\n"])),
    );

    await expect(
      streamChat(request, new AbortController().signal),
    ).rejects.toThrow("Invalid chat stream: malformed delta event");
  });

  it("rejects a stream that ends before done, including a trailing unterminated frame", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () =>
        sseResponse([
          frame("delta", { text: "半句" }),
          `event: done\ndata: ${JSON.stringify(doneResponse)}`,
        ]),
      ),
    );

    await expect(
      streamChat(request, new AbortController().signal),
    ).rejects.toThrow("Chat stream ended before completion");
  });
});
