import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import type {
  ChatRequest,
  ChatResponse,
  CzmlPacket,
  SceneSummary,
} from "../contracts/chat";
import { App, type AppSceneManager, type ChatClient } from "./App";

function ViewerStub() {
  return <div aria-label="Test viewer" />;
}

function createManager(
  overrides: Partial<AppSceneManager> = {},
): AppSceneManager {
  const summary: SceneSummary = {
    entities: [
      {
        id: "sanya",
        name: "Sanya Ground Station",
        type: "facility",
        lon: 109.5,
        lat: 18.2,
        alt: 10,
      },
      { id: "beijing", name: "Beijing", type: "facility" },
    ],
  };
  const packets: Record<string, CzmlPacket> = {
    sanya: {
      id: "sanya",
      name: "Sanya Ground Station",
      position: { cartographicDegrees: [109.5, 18.2, 10] },
      point: { pixelSize: 10 },
    },
    beijing: { id: "beijing", name: "Beijing" },
  };

  return {
    initialize: vi.fn(async () => undefined),
    setSelectedEntityIds: vi.fn(),
    buildSummary: vi.fn(() => summary),
    getSelectedEntityIds: vi.fn(() => ["sanya"]),
    pickRelevantPackets: vi.fn((ids: string[]) =>
      ids.map((id) => packets[id]).filter((packet) => packet !== undefined),
    ),
    applySceneOps: vi.fn(async () => undefined),
    ...overrides,
  };
}

function renderApp(sceneManager: AppSceneManager, chatClient: ChatClient) {
  return render(
    <App
      sceneManager={sceneManager}
      chatClient={chatClient}
      ViewerComponent={ViewerStub}
    />,
  );
}

it("renders the viewer and chat product shell", () => {
  renderApp(
    createManager(),
    vi.fn(async () => ({ sessionId: "s1", message: "ok", sceneOps: [] })),
  );

  expect(screen.getByRole("main", { name: "CesiumAI" })).toBeInTheDocument();
  expect(screen.getByLabelText("Test viewer")).toBeInTheDocument();
  expect(screen.getByLabelText("场景助手")).toBeInTheDocument();
});

it("assembles scene context, reuses the session, and applies each response once", async () => {
  const user = userEvent.setup();
  const manager = createManager();
  const responses: ChatResponse[] = [
    {
      sessionId: "session-1",
      message: "已将 Sanya 高度改为 50。",
      sceneOps: [
        {
          op: "upsert",
          packets: [
            {
              id: "sanya",
              position: { cartographicDegrees: [109.5, 18.2, 50] },
            },
          ],
        },
      ],
    },
    {
      sessionId: "session-1",
      message: "高度仍为 50。",
      sceneOps: [],
    },
  ];
  const chatClient = vi
    .fn<ChatClient>()
    .mockResolvedValueOnce(responses[0]!)
    .mockResolvedValueOnce(responses[1]!);
  renderApp(manager, chatClient);

  await user.type(
    screen.getByLabelText("消息"),
    "把 sanya 高度改为 50{Enter}",
  );

  expect(await screen.findByText("已将 Sanya 高度改为 50。")).toBeInTheDocument();
  expect(manager.buildSummary).toHaveBeenCalledOnce();
  expect(manager.getSelectedEntityIds).toHaveBeenCalledOnce();
  expect(manager.pickRelevantPackets).toHaveBeenCalledWith(["sanya"]);
  expect(chatClient).toHaveBeenNthCalledWith(
    1,
    {
      message: "把 sanya 高度改为 50",
      sessionId: null,
      sceneSummary: expect.objectContaining({
        entities: expect.arrayContaining([
          expect.objectContaining({ id: "sanya" }),
        ]),
      }),
      relevantPackets: [
        expect.objectContaining({
          id: "sanya",
          point: { pixelSize: 10 },
          position: { cartographicDegrees: [109.5, 18.2, 10] },
        }),
      ],
    },
    expect.any(AbortSignal),
  );
  expect(manager.applySceneOps).toHaveBeenCalledOnce();
  expect(manager.applySceneOps).toHaveBeenCalledWith(responses[0]!.sceneOps);
  await waitFor(() => expect(screen.getByLabelText("消息")).toBeEnabled());

  await user.type(screen.getByLabelText("消息"), "现在多高？{Enter}");

  expect(await screen.findByText("高度仍为 50。")).toBeInTheDocument();
  expect(chatClient).toHaveBeenNthCalledWith(
    2,
    expect.objectContaining({
      message: "现在多高？",
      sessionId: "session-1",
    }),
    expect.any(AbortSignal),
  );
  expect(manager.applySceneOps).toHaveBeenCalledTimes(2);
  expect(manager.applySceneOps).toHaveBeenNthCalledWith(
    2,
    responses[1]!.sceneOps,
  );
});

it("shows API errors without retrying", async () => {
  const user = userEvent.setup();
  const chatClient = vi.fn<ChatClient>(async (_request: ChatRequest) => {
    throw new Error("服务不可用");
  });
  renderApp(createManager(), chatClient);

  await user.type(screen.getByLabelText("消息"), "更新场景{Enter}");

  expect(await screen.findByRole("alert")).toHaveTextContent("服务不可用");
  expect(chatClient).toHaveBeenCalledOnce();
});

it("shows apply errors without reapplying scene operations", async () => {
  const user = userEvent.setup();
  const applySceneOps = vi.fn(async () => {
    throw new Error("场景更新失败");
  });
  const manager = createManager({ applySceneOps });
  const chatClient = vi.fn<ChatClient>(async () => ({
    sessionId: "session-1",
    message: "模型已响应",
    sceneOps: [{ op: "clear" }],
  }));
  renderApp(manager, chatClient);

  await user.type(screen.getByLabelText("消息"), "清空{Enter}");

  expect(await screen.findByText("模型已响应")).toBeInTheDocument();
  expect(await screen.findByRole("alert")).toHaveTextContent("场景更新失败");
  await waitFor(() => expect(applySceneOps).toHaveBeenCalledOnce());
  expect(chatClient).toHaveBeenCalledOnce();
});
