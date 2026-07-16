import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ChatRequest } from "../contracts/chat";
import { postChat } from "./chat";

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
});
