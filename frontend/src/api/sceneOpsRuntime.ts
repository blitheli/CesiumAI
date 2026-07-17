import type { CameraSceneOp, CzmlPacket, SceneOp } from "../contracts/chat";

const CAMERA_ACTIONS = new Set<CameraSceneOp["action"]>([
  "focus",
  "track",
  "untrack",
  "zoom",
  "pan",
  "rotate",
  "orbitStep",
  "orbitStart",
  "orbitStop",
]);

const PAN_DIRECTIONS = new Set(["left", "right", "up", "down"]);

const NUMERIC_CAMERA_KEYS = [
  "distanceMeters",
  "headingDegrees",
  "pitchDegrees",
  "rollDegrees",
  "amount",
  "angularSpeedDegreesPerSecond",
] as const;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function isCzmlPacket(value: unknown): value is CzmlPacket {
  return isRecord(value) && isNonEmptyString(value.id);
}

function isNonEmptyStringArray(value: unknown): value is string[] {
  return (
    Array.isArray(value) &&
    value.length > 0 &&
    value.every((item) => isNonEmptyString(item))
  );
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

function isAbsentOrNull(value: unknown): boolean {
  return value === undefined || value === null;
}

/** 出现的数值必须有限；缺省/null 允许。 */
function optionalFinite(value: unknown): boolean {
  return isAbsentOrNull(value) || isFiniteNumber(value);
}

function optionalPositiveDistance(value: unknown): boolean {
  return isAbsentOrNull(value) || (isFiniteNumber(value) && value > 0);
}

function hasNonEmptyTargetId(value: Record<string, unknown>): boolean {
  return isNonEmptyString(value.targetId);
}

function allPresentNumericsFinite(value: Record<string, unknown>): boolean {
  for (const key of NUMERIC_CAMERA_KEYS) {
    if (!optionalFinite(value[key])) {
      return false;
    }
  }
  return true;
}

function directionOk(value: Record<string, unknown>, required: boolean): boolean {
  const direction = value.direction;
  if (isAbsentOrNull(direction)) {
    return !required;
  }
  return typeof direction === "string" && PAN_DIRECTIONS.has(direction);
}

function isCameraSceneOp(value: Record<string, unknown>): boolean {
  if (
    typeof value.action !== "string" ||
    !CAMERA_ACTIONS.has(value.action as CameraSceneOp["action"])
  ) {
    return false;
  }

  if (!allPresentNumericsFinite(value)) {
    return false;
  }

  // direction 若出现必须是已知枚举（非 pan 也校验，防畸形载荷）。
  if (!directionOk(value, value.action === "pan")) {
    return false;
  }

  switch (value.action) {
    case "focus":
      return (
        hasNonEmptyTargetId(value) &&
        optionalPositiveDistance(value.distanceMeters)
      );
    case "track":
      return hasNonEmptyTargetId(value);
    case "untrack":
    case "orbitStop":
      // 不强制 targetId/amount 等危险必填字段。
      return true;
    case "zoom":
      return (
        isFiniteNumber(value.amount) && value.amount !== 0
      );
    case "pan":
      return (
        directionOk(value, true) &&
        isFiniteNumber(value.amount) &&
        value.amount > 0
      );
    case "rotate": {
      const angles = [
        value.headingDegrees,
        value.pitchDegrees,
        value.rollDegrees,
      ];
      return angles.some(
        (angle) => isFiniteNumber(angle) && angle !== 0,
      );
    }
    case "orbitStep":
      return (
        hasNonEmptyTargetId(value) &&
        isFiniteNumber(value.amount) &&
        value.amount !== 0 &&
        optionalPositiveDistance(value.distanceMeters)
      );
    case "orbitStart":
      return (
        hasNonEmptyTargetId(value) &&
        isFiniteNumber(value.angularSpeedDegreesPerSecond) &&
        value.angularSpeedDegreesPerSecond > 0 &&
        optionalPositiveDistance(value.distanceMeters)
      );
    default:
      return false;
  }
}

/** 运行时判别单个 SceneOp；畸形则返回 false。 */
export function isSceneOp(value: unknown): value is SceneOp {
  if (!isRecord(value) || typeof value.op !== "string") {
    return false;
  }

  switch (value.op) {
    case "clear":
      return true;
    case "upsert":
      return (
        Array.isArray(value.packets) &&
        value.packets.length > 0 &&
        value.packets.every(isCzmlPacket)
      );
    case "delete":
      return isNonEmptyStringArray(value.ids);
    case "camera":
      return isCameraSceneOp(value);
    case "style":
      return (
        isNonEmptyString(value.id) && isRecord(value.patch)
      );
    default:
      return false;
  }
}

/** 校验 sceneOps 数组；未知 op 或畸形则整体拒绝。 */
export function isSceneOpArray(value: unknown): value is SceneOp[] {
  return Array.isArray(value) && value.every(isSceneOp);
}
