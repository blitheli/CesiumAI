import type { CzmlPacket } from "../contracts/chat";

export function createEmptyDocument(now: Date): CzmlPacket[] {
  const startIso = now.toISOString();
  const stopDate = new Date(now.getTime() + 24 * 60 * 60 * 1000);
  const stopIso = stopDate.toISOString();

  return [
    {
      id: "document",
      name: "CesiumAI Scene",
      version: "1.0",
      clock: {
        interval: `${startIso}/${stopIso}`,
        currentTime: startIso,
        multiplier: 60,
      },
    },
  ];
}
