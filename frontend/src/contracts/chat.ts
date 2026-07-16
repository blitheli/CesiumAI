export type CzmlPacket = { id: string } & Record<string, unknown>;

export type ClearSceneOp = { op: "clear" };
export type UpsertSceneOp = { op: "upsert"; packets: CzmlPacket[] };
export type DeleteSceneOp = { op: "delete"; ids: string[] };
export type SceneOp = ClearSceneOp | UpsertSceneOp | DeleteSceneOp;

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
