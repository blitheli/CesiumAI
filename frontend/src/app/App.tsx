import {
  useEffect,
  useRef,
  useState,
  type ComponentType,
} from "react";
import { streamChat, type ChatStreamListener } from "../api/chat";
import { ChatPanel, type UiMessage } from "../components/ChatPanel";
import {
  ViewerHost,
  type ViewerHostProps,
  type ViewerSceneManager,
} from "../components/ViewerHost";
import type {
  ChatRequest,
  ChatResponse,
  CzmlPacket,
  SceneOp,
  SceneSummary,
} from "../contracts/chat";
import { assertNever } from "../contracts/assertNever";
import type { SceneDiagnostics } from "../scene/CesiumSceneManager";
import { inferRelevantEntityIds } from "../scene/summary";
import "../styles.css";

export interface AppSceneManager extends ViewerSceneManager {
  buildSummary(): SceneSummary;
  getSelectedEntityIds(): string[];
  pickRelevantPackets(ids: string[]): CzmlPacket[];
  applySceneOps(operations: SceneOp[]): Promise<void>;
  getSceneDiagnostics(): SceneDiagnostics;
}

/**
 * 聊天客户端：可通过 `onEvent` 推送流式进度，最终以 ChatResponse 结束。
 * 返回值是本轮的权威结果（最终文本与 sceneOps）；只推进度、不推事件的实现同样合法。
 */
export type ChatClient = (
  request: ChatRequest,
  signal: AbortSignal,
  onEvent?: ChatStreamListener,
) => Promise<ChatResponse>;

export type AppProps = {
  sceneManager: AppSceneManager;
  chatClient?: ChatClient;
  ViewerComponent?: ComponentType<ViewerHostProps>;
};

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : "请求失败，请稍后再试。";
}

/** 仅测试/验收构建启用只读 diagnostics；生产默认关闭。 */
export const testDiagnosticsEnabled =
  import.meta.env.VITE_ENABLE_TEST_DIAGNOSTICS === "true";

export function App({
  sceneManager,
  chatClient = streamChat,
  ViewerComponent = ViewerHost,
}: AppProps) {
  const [messages, setMessages] = useState<UiMessage[]>([]);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [activity, setActivity] = useState<string | null>(null);
  const [sceneDiagnostics, setSceneDiagnostics] =
    useState<SceneDiagnostics | null>(null);
  const messageSequence = useRef(0);
  const activeControllers = useRef(new Set<AbortController>());

  useEffect(
    () => () => {
      for (const controller of activeControllers.current) {
        controller.abort();
      }
    },
    [],
  );

  // 仅在显式测试开关下挂载只读 live diagnostics；正常生产构建不得暴露 window 全局。
  useEffect(() => {
    if (!testDiagnosticsEnabled) {
      return;
    }

    window.__CESIUM_AI_READ_DIAGNOSTICS__ = () =>
      sceneManager.getSceneDiagnostics();
    return () => {
      delete window.__CESIUM_AI_READ_DIAGNOSTICS__;
    };
  }, [sceneManager]);

  const nextMessageId = () => {
    messageSequence.current += 1;
    return `message-${messageSequence.current}`;
  };

  const addMessage = (role: UiMessage["role"], text: string) => {
    const message: UiMessage = { id: nextMessageId(), role, text };
    setMessages((current) => [...current, message]);
  };

  /** 新增或原地更新同一条助手消息（流式增量与最终文本共用）。 */
  const upsertMessage = (message: UiMessage) => {
    setMessages((current) =>
      current.some((existing) => existing.id === message.id)
        ? current.map((existing) =>
            existing.id === message.id ? message : existing,
          )
        : [...current, message],
    );
  };

  const handleSend = async (text: string) => {
    if (loading) {
      return;
    }

    addMessage("user", text);
    setLoading(true);
    setError(null);
    setActivity(null);
    const controller = new AbortController();
    activeControllers.current.add(controller);

    const assistantId = nextMessageId();
    let streamedText = "";
    let assistantVisible = false;
    const toolNames = new Map<string, string>();

    const onEvent: ChatStreamListener = (event) => {
      switch (event.type) {
        case "session":
          // 服务端已为该会话建立 Agent 历史；即使本轮随后失败也应沿用。
          setSessionId(event.sessionId);
          break;
        case "delta":
          streamedText += event.text;
          assistantVisible = true;
          upsertMessage({
            id: assistantId,
            role: "assistant",
            text: streamedText,
            streaming: true,
          });
          break;
        case "tool_call":
          toolNames.set(event.callId, event.name);
          setActivity(`正在调用工具 ${event.name}…`);
          break;
        case "tool_result": {
          const name = toolNames.get(event.callId) ?? "工具";
          setActivity(
            event.succeeded ? `${name} 已完成，继续处理…` : `${name} 调用失败，继续处理…`,
          );
          break;
        }
        case "done":
        case "error":
          break;
        default:
          assertNever(event);
      }
    };

    try {
      const summary = sceneManager.buildSummary();
      const relevantIds = inferRelevantEntityIds(
        text,
        summary,
        sceneManager.getSelectedEntityIds(),
      );
      const relevantPackets = sceneManager.pickRelevantPackets(relevantIds);
      const response = await chatClient(
        {
          message: text,
          sessionId,
          sceneSummary: summary,
          relevantPackets,
        },
        controller.signal,
        onEvent,
      );

      setSessionId(response.sessionId);
      assistantVisible = true;
      upsertMessage({ id: assistantId, role: "assistant", text: response.message });
      // 相机飞行等场景操作可能耗时数秒，此时回复已完整，状态改为提示正在更新场景。
      setActivity(response.sceneOps.length > 0 ? "正在更新场景…" : null);
      await sceneManager.applySceneOps(response.sceneOps);
      if (testDiagnosticsEnabled) {
        setSceneDiagnostics(sceneManager.getSceneDiagnostics());
      }
    } catch (requestError) {
      if (assistantVisible) {
        // 保留已流出的部分文本，但结束“输入中”状态。
        setMessages((current) =>
          current.map((message) =>
            message.id === assistantId && message.streaming
              ? { ...message, streaming: false }
              : message,
          ),
        );
      }
      setError(errorMessage(requestError));
    } finally {
      activeControllers.current.delete(controller);
      setActivity(null);
      setLoading(false);
    }
  };

  return (
    <main className="app-shell" aria-label="CesiumAI">
      <section className="viewer-pane" aria-label="三维场景">
        <ViewerComponent sceneManager={sceneManager} />
        {testDiagnosticsEnabled && sceneDiagnostics ? (
          <output
            className="scene-diagnostics"
            aria-label="场景诊断"
            data-scene-diagnostics={JSON.stringify(sceneDiagnostics)}
          >
            {sceneDiagnostics.entities.length} 个实体
            {sceneDiagnostics.clock
              ? ` · 时钟 ${sceneDiagnostics.clock.currentTime}`
              : ""}
          </output>
        ) : null}
      </section>
      <ChatPanel
        messages={messages}
        loading={loading}
        activity={activity}
        error={error}
        onSend={handleSend}
      />
    </main>
  );
}
