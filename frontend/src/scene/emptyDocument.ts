import type { CzmlPacket } from "../contracts/chat";

/**
 * 创建一个空的 CZML 文档，包含一个 document packet。
 * 
 * 在这里进行初始czml的设置!
 * 
 * TBD: 从文件夹或后端获取各种模板!
 * 
 * @param now - 当前时间，用于设置 clock 的 interval 和 currentTime。
 * @returns 一个空的 CZML 文档，包含一个 document packet。
 */
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
