export type CzmlPacket = { id: string } & Record<string, unknown>;

export type ClearSceneOp = { op: "clear" };
export type UpsertSceneOp = { op: "upsert"; packets: CzmlPacket[] };
export type DeleteSceneOp = { op: "delete"; ids: string[] };

/** 与后端 CameraSceneOp / CameraAction 线格式一致（camelCase；可空字段可显式为 null）。 */
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
  targetId?: string | null;
  distanceMeters?: number | null;
  headingDegrees?: number | null;
  pitchDegrees?: number | null;
  rollDegrees?: number | null;
  amount?: number | null;
  direction?: "left" | "right" | "up" | "down" | null;
  angularSpeedDegreesPerSecond?: number | null;
};

/** 与后端 StyleSceneOp 线格式一致。 */
export type StyleSceneOp = {
  op: "style";
  id: string;
  patch: Record<string, unknown>;
};

export type SceneOp =
  | ClearSceneOp
  | UpsertSceneOp
  | DeleteSceneOp
  | CameraSceneOp
  | StyleSceneOp;

export type EntitySummary = {
  id: string;
  name?: string;
  type: "facility" | "satellite" | "other";
  lon?: number;
  lat?: number;
  alt?: number;
  orbitHint?: string;
};

export type SceneSummary = {
  documentClock?: { interval?: string; currentTime?: string };
  entities: EntitySummary[];
};

export type ChatRequest = {
  message: string;
  sessionId?: string | null;
  sceneSummary: SceneSummary;
  relevantPackets?: CzmlPacket[];
};

export type ChatResponse = {
  sessionId: string;
  message: string;
  sceneOps: SceneOp[];
};
