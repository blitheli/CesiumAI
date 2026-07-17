import {
  useEffect,
  useRef,
  useState,
  type ComponentType,
} from "react";
import { postChat } from "../api/chat";
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

export type ChatClient = (
  request: ChatRequest,
  signal: AbortSignal,
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
  chatClient = postChat,
  ViewerComponent = ViewerHost,
}: AppProps) {
  const [messages, setMessages] = useState<UiMessage[]>([]);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
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

  const addMessage = (role: UiMessage["role"], text: string) => {
    messageSequence.current += 1;
    const message: UiMessage = {
      id: `message-${messageSequence.current}`,
      role,
      text,
    };
    setMessages((current) => [...current, message]);
  };

  const handleSend = async (text: string) => {
    if (loading) {
      return;
    }

    addMessage("user", text);
    setLoading(true);
    setError(null);
    const controller = new AbortController();
    activeControllers.current.add(controller);

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
      );

      setSessionId(response.sessionId);
      addMessage("assistant", response.message);
      await sceneManager.applySceneOps(response.sceneOps);
      if (testDiagnosticsEnabled) {
        setSceneDiagnostics(sceneManager.getSceneDiagnostics());
      }
    } catch (requestError) {
      setError(errorMessage(requestError));
    } finally {
      activeControllers.current.delete(controller);
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
        error={error}
        onSend={handleSend}
      />
    </main>
  );
}
