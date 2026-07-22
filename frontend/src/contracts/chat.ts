/**
 * 前后端共享的聊天 / 场景线格式类型（JSON camelCase）。
 *
 * 与后端 `ChatContracts` / `SceneOps` / `CameraCommands` 对齐。
 * 前端权威持有 CZML 文档；后端通过 `ChatResponse.sceneOps` 下发变更意图，
 * 由 `CesiumSceneManager.applySceneOps` 落到内存文档与 Viewer。
 *
 * 详见 Docs/前端说明.md §4.1。
 */

/**
 * 单个 CZML packet 的宽松表示。
 * 至少含非空 `id`；其余字段（position、point、model 等）按 CZML 约定自由扩展。
 * `id === "document"` 为场景根包，由前端维护，业务 upsert 应忽略对其的改写。
 */
export type CzmlPacket = { id: string } & Record<string, unknown>;

/** 清空全部业务实体，回到空文档（仅保留 document packet）。 */
export type ClearSceneOp = { op: "clear" };

/**
 * 按 id 新增或整包替换实体 CZML。
 * 改 `position` / `availability` 等轨道数据只能走 upsert，不能靠 style。
 */
export type UpsertSceneOp = { op: "upsert"; packets: CzmlPacket[] };

/** 按 id 删除业务实体；document 不可删。 */
export type DeleteSceneOp = { op: "delete"; ids: string[] };

/**
 * 相机动作（不改 CZML 文档，由 `CesiumCameraController` 解释）。
 * 与后端 CameraSceneOp / CameraAction 线格式一致（camelCase；可空字段可显式为 null）。
 *
 * action 语义概要：
 * - focus：飞到目标实体
 * - track / untrack：跟踪 / 取消跟踪
 * - zoom / pan / rotate：相对调整
 * - orbitStart / orbitStep / orbitStop：环绕
 */
export type CameraSceneOp = {
  op: "camera";
  action:
    | "focus"
    | "track"
    | "untrack"
    | "zoom"
    | "pan"
    | "rotate"
    | "orbitStep"
    | "orbitStart"
    | "orbitStop";
  /** 焦点/跟踪目标实体 id（focus、track、部分 orbit 需要）。 */
  targetId?: string | null;
  /** focus 等：相机到目标的距离（米）。 */
  distanceMeters?: number | null;
  headingDegrees?: number | null;
  pitchDegrees?: number | null;
  rollDegrees?: number | null;
  /** zoom / pan / rotate / orbitStep 等的相对幅度。 */
  amount?: number | null;
  /** pan 等方向。 */
  direction?: "left" | "right" | "up" | "down" | null;
  /** orbitStart：环绕角速度（度/秒）。 */
  angularSpeedDegreesPerSecond?: number | null;
};

/**
 * 对已有实体做视觉样式补丁（与后端 StyleSceneOp 一致）。
 * `patch` 仅允许视觉键（point/path/label/...）；经 `sceneStyle.applyStylePatch` 校验与深合并。
 * 不可通过 patch 改 position，也不可设置 billboard/model 的外部资源 URI。
 */
export type StyleSceneOp = {
  op: "style";
  /** 目标实体 id（不能是 document）。 */
  id: string;
  /** 视觉补丁对象；结构由前后端 SceneStyle 白名单约束。 */
  patch: Record<string, unknown>;
};

/**
 * 场景操作白名单联合类型（五种 op）。
 * 运行时另有 `api/sceneOpsRuntime` 护栏，防止畸形数据进入 applySceneOps。
 */
export type SceneOp =
  | ClearSceneOp
  | UpsertSceneOp
  | DeleteSceneOp
  | CameraSceneOp
  | StyleSceneOp;

/**
 * 单个实体的轻量摘要（发给后端，避免塞全量 CZML）。
 * 由 `summary.buildSceneSummary` 从内存文档生成。
 */
export type EntitySummary = {
  id: string;
  name?: string;
  type: "facility" | "satellite" | "other";
  /** 设施等：大致经纬高（度 / 米）。 */
  lon?: number;
  lat?: number;
  alt?: number;
  /** 卫星等：轨道提示（如 sgp4 / sso）。 */
  orbitHint?: string;
};

/** 当前场景摘要：时钟窗口 + 实体列表。 */
export type SceneSummary = {
  documentClock?: { interval?: string; currentTime?: string };
  entities: EntitySummary[];
};

/**
 * `POST /api/chat` 请求体。
 * 请求体不带全量场景；用摘要 + 可选相关 packet 控制体积。
 */
export type ChatRequest = {
  /** 用户自然语言。 */
  message: string;
  /** 可选；有则续聊，无则后端新建会话。 */
  sessionId?: string | null;
  /** 场景轻量摘要。 */
  sceneSummary: SceneSummary;
  /** 可选；本轮推断出的相关实体完整 CZML（由 pickRelevantPackets 等提供）。 */
  relevantPackets?: CzmlPacket[];
};

/**
 * `POST /api/chat` 响应体。
 * 前端应保存 `sessionId` 供下次请求；用 `sceneOps` 更新球与文档。
 */
export type ChatResponse = {
  sessionId: string;
  /** 助手自然语言回复。 */
  message: string;
  /** 场景变更意图；可为 `[]`。 */
  sceneOps: SceneOp[];
};
