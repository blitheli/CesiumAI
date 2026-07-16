import {
  expect,
  test,
  type ConsoleMessage,
  type Page,
} from "@playwright/test";

type ChatRequest = {
  message: string;
  sessionId?: string | null;
  sceneSummary?: {
    entities?: Array<Record<string, unknown>>;
  };
  relevantPackets?: Array<Record<string, unknown>>;
};

type ChatResponse = {
  sessionId: string;
  message: string;
  sceneOps: Array<Record<string, unknown>>;
};

type SceneDiagnostics = {
  clock?: {
    startTime: string;
    stopTime: string;
    currentTime: string;
  };
  camera?: {
    trackedEntityId: string | null;
    orbitActive: boolean;
    orbitTargetId: string | null;
    orbitHeadingDegrees: number | null;
    headingDegrees: number | null;
    positionWC: [number, number, number] | null;
  };
  entities: Array<{
    id: string;
    hasPosition: boolean;
    hasPositionAtCurrentTime: boolean;
    hasPoint: boolean;
    hasPath: boolean;
    hasCanonicalPosition?: boolean;
    canonicalPositionSampleCount?: number;
    positionAtCurrentTime?: [number, number, number];
    pointPixelSize?: number;
    pointColorRgba?: [number, number, number, number];
    pathWidth?: number;
  }>;
};

const facilityPacket = {
  id: "acceptance-facility",
  name: "Acceptance Ground Station",
  position: { cartographicDegrees: [-100, 30.2, 10] },
  point: {
    color: { rgba: [0, 255, 255, 255] },
    pixelSize: 12,
  },
};

const updatedFacilityPacket = {
  ...facilityPacket,
  position: { cartographicDegrees: [-100, 30.2, 50] },
};

const satellitePacket = {
  id: "acceptance-satellite",
  name: "Acceptance 900 km SSO",
  availability: "2026-01-01T00:00:00Z/2026-01-02T00:00:00Z",
  position: {
    epoch: "2026-01-01T00:00:00Z",
    cartesianVelocity: [
      0, 7271000, 0, 0, 0, 1000, 7400,
      43200, -3600000, 6200000, 800000, -6500, -3600, 700,
      86400, -3700000, -6100000, -900000, 6400, -3700, -800,
    ],
  },
  point: {
    pixelSize: 8,
    color: { rgba: [255, 220, 0, 255] },
  },
  path: {
    show: true,
    width: 2,
    leadTime: 0,
    trailTime: 86400,
    material: {
      solidColor: { color: { rgba: [0, 200, 255, 220] } },
    },
  },
  properties: { orbitHint: { string: "900 km SSO / J2" } },
};

const issPacket = {
  id: "iss",
  name: "国际空间站",
  availability: "2026-01-01T00:00:00Z/2026-01-02T00:00:00Z",
  position: {
    epoch: "2026-01-01T00:00:00Z",
    cartesianVelocity: [
      0, 7271000, 0, 0, 0, 1000, 7400,
      43200, -3600000, 6200000, 800000, -6500, -3600, 700,
      86400, -3700000, -6100000, -900000, 6400, -3700, -800,
    ],
  },
  point: {
    pixelSize: 10,
    color: { rgba: [255, 220, 0, 255] },
  },
  path: {
    show: true,
    width: 2,
    leadTime: 0,
    trailTime: 86400,
    material: {
      solidColor: { color: { rgba: [0, 200, 255, 220] } },
    },
  },
  properties: { orbitHint: { string: "ISS / SGP4" } },
};

function response(
  message: string,
  sceneOps: ChatResponse["sceneOps"] = [],
): ChatResponse {
  return { sessionId: "acceptance-session", message, sceneOps };
}

async function expectSceneContext(request: ChatRequest) {
  expect(request.sceneSummary).toEqual(
    expect.objectContaining({ entities: expect.any(Array) }),
  );
}

async function expectSameCanvas(page: Page) {
  await expect(
    page.locator(
      '.cesium-widget canvas[data-e2e-persistent-canvas="true"]',
    ),
  ).toHaveCount(1);
}

async function readSceneDiagnostics(page: Page): Promise<SceneDiagnostics> {
  const output = page.getByLabel("场景诊断");
  await expect(output).toBeVisible();
  const serialized = await output.getAttribute("data-scene-diagnostics");
  expect(serialized).not.toBeNull();
  return JSON.parse(serialized!) as SceneDiagnostics;
}

/** 读取生产只读 live diagnostics（不经过聊天回合缓存）。 */
async function readLiveSceneDiagnostics(
  page: Page,
): Promise<SceneDiagnostics> {
  const diagnostics = await page.evaluate(() => {
    const reader = window.__CESIUM_AI_READ_DIAGNOSTICS__;
    if (typeof reader !== "function") {
      throw new Error("只读 diagnostics 读取器未挂载。");
    }
    return reader();
  });
  return diagnostics as SceneDiagnostics;
}

async function sendCommand(
  page: Page,
  command: string,
  assistantText: string,
) {
  await page.getByLabel("消息").fill(command);
  await page.getByRole("button", { name: "发送" }).click();
  await expect(
    page.locator('[data-role="assistant"] p').filter({ hasText: assistantText }),
  ).toBeVisible();
  await expect(page.getByLabel("消息")).toBeEnabled();
  await expectSameCanvas(page);
}

async function openApp(
  page: Page,
  handler: (request: ChatRequest) => ChatResponse,
) {
  const requests: ChatRequest[] = [];
  const browserErrors: string[] = [];
  const captureConsoleError = (message: ConsoleMessage) => {
    if (message.type() === "error") {
      browserErrors.push(message.text());
    }
  };

  page.on("console", captureConsoleError);
  page.on("pageerror", (error) => browserErrors.push(error.message));
  await page.route("**/api/chat", async (route) => {
    expect(route.request().method()).toBe("POST");
    const request = route.request().postDataJSON() as ChatRequest;
    requests.push(request);
    await expectSceneContext(request);
    await route.fulfill({ json: handler(request) });
  });

  await page.goto("/");
  const canvas = await page
    .locator(".cesium-widget canvas")
    .first()
    .elementHandle();
  expect(canvas).not.toBeNull();
  await canvas?.evaluate((element) => {
    element.dataset.e2ePersistentCanvas = "true";
  });

  return {
    requests,
    browserErrors,
  };
}

test("clear resets the scene while the Cesium canvas stays mounted", async ({
  page,
}) => {
  const { requests, browserErrors } = await openApp(page, (request) => {
    if (request.message === "添加验收地面站") {
      return response("已添加验收地面站。", [
        { op: "upsert", packets: [facilityPacket] },
      ]);
    }
    if (request.message === "清空当前场景") {
      return response("已清空当前场景。", [{ op: "clear" }]);
    }
    return response("当前场景为空。");
  });

  await sendCommand(page, "添加验收地面站", "已添加验收地面站。");
  await sendCommand(page, "清空当前场景", "已清空当前场景。");
  await sendCommand(page, "列出当前场景", "当前场景为空。");

  expect(requests[1]?.sceneSummary?.entities).toEqual([
    expect.objectContaining({ id: facilityPacket.id }),
  ]);
  expect(requests[2]?.sceneSummary?.entities).toEqual([]);
  expect((await readSceneDiagnostics(page)).entities).toEqual([]);
  expect(browserErrors).toEqual([]);
});

test("facility add sends scene context and makes the packet relevant by name", async ({
  page,
}) => {
  const { requests, browserErrors } = await openApp(page, (request) =>
    request.message.startsWith("添加一个地面站")
      ? response("已添加 Acceptance Ground Station。", [
          { op: "upsert", packets: [facilityPacket] },
        ])
      : response("地面站已在场景中。"),
  );

  await sendCommand(
    page,
    "添加一个地面站，经纬高是 -100, 30.2, 10",
    "已添加 Acceptance Ground Station。",
  );
  await sendCommand(
    page,
    "查询 Acceptance Ground Station",
    "地面站已在场景中。",
  );

  expect(requests[0]?.sceneSummary?.entities).toEqual([]);
  expect(requests[1]?.sceneSummary?.entities).toContainEqual(
    expect.objectContaining({
      id: facilityPacket.id,
      lon: -100,
      lat: 30.2,
      alt: 10,
    }),
  );
  expect(requests[1]?.relevantPackets).toContainEqual(facilityPacket);
  expect((await readSceneDiagnostics(page)).entities).toContainEqual(
    expect.objectContaining({
      id: facilityPacket.id,
      hasPosition: true,
      hasPositionAtCurrentTime: true,
      hasPoint: true,
    }),
  );
  expect(browserErrors).toEqual([]);
});

test("facility update replaces the named entity packet at a static position", async ({
  page,
}) => {
  const { requests, browserErrors } = await openApp(page, (request) => {
    if (request.message === "添加验收地面站") {
      return response("已添加验收地面站。", [
        { op: "upsert", packets: [facilityPacket] },
      ]);
    }
    if (request.message.startsWith("把 Acceptance Ground Station")) {
      return response("已将地面站高度改为 50 米。", [
        { op: "upsert", packets: [updatedFacilityPacket] },
      ]);
    }
    return response("地面站高度为 50 米。");
  });

  await sendCommand(page, "添加验收地面站", "已添加验收地面站。");
  const positionBeforeUpdate = (await readSceneDiagnostics(page)).entities[0]
    ?.positionAtCurrentTime;
  await sendCommand(
    page,
    "把 Acceptance Ground Station 高度改为 50 米",
    "已将地面站高度改为 50 米。",
  );
  await sendCommand(
    page,
    "查询 Acceptance Ground Station 高度",
    "地面站高度为 50 米。",
  );

  expect(requests[1]?.relevantPackets).toContainEqual(facilityPacket);
  expect(requests[2]?.sceneSummary?.entities).toContainEqual(
    expect.objectContaining({ id: facilityPacket.id, alt: 50 }),
  );
  expect(requests[2]?.relevantPackets).toContainEqual(updatedFacilityPacket);
  const updatedDiagnostics = await readSceneDiagnostics(page);
  expect(updatedDiagnostics.entities[0]).toEqual(
    expect.objectContaining({
      id: facilityPacket.id,
      hasPositionAtCurrentTime: true,
      hasPoint: true,
    }),
  );
  expect(updatedDiagnostics.entities[0]?.positionAtCurrentTime).not.toEqual(
    positionBeforeUpdate,
  );
  expect(browserErrors).toEqual([]);
});

test("satellite add accepts a one-day cartesianVelocity trajectory", async ({
  page,
}) => {
  const { requests, browserErrors } = await openApp(page, (request) =>
    request.message.startsWith("添加一个 900km SSO")
      ? response("已添加 900km SSO 卫星和一天 J2 星历。", [
          { op: "upsert", packets: [satellitePacket] },
        ])
      : response("卫星轨迹已在场景中。"),
  );

  await sendCommand(
    page,
    "添加一个 900km SSO 卫星，使用 J2 递推一天",
    "已添加 900km SSO 卫星和一天 J2 星历。",
  );
  const firstDiagnostics = await readSceneDiagnostics(page);
  const firstPosition =
    firstDiagnostics.entities[0]?.positionAtCurrentTime;
  expect(firstDiagnostics.entities).toContainEqual(
    expect.objectContaining({
      id: satellitePacket.id,
      hasPosition: true,
      hasPositionAtCurrentTime: true,
      hasPath: true,
    }),
  );
  expect(firstPosition).toHaveLength(3);
  expect(firstDiagnostics.clock).toBeDefined();
  expect(Date.parse(firstDiagnostics.clock!.currentTime)).toBeGreaterThanOrEqual(
    Date.parse(satellitePacket.availability.split("/")[0]!),
  );
  expect(Date.parse(firstDiagnostics.clock!.currentTime)).toBeLessThanOrEqual(
    Date.parse(satellitePacket.availability.split("/")[1]!),
  );

  const playForward = page.locator(
    'g.cesium-animation-rectButton:has(title:text-is("Play Forward"))',
  );
  const pause = page.locator(
    'g.cesium-animation-rectButton:has(title:text-is("Pause"))',
  );
  await expect(pause).toHaveClass(/cesium-animation-buttonToggled/);
  await expect(playForward).toBeVisible();
  await playForward.click();
  await expect(playForward).toHaveClass(/cesium-animation-buttonToggled/);
  await page.waitForTimeout(250);
  await sendCommand(
    page,
    "查询 Acceptance 900 km SSO",
    "卫星轨迹已在场景中。",
  );

  expect(satellitePacket.availability).toContain("/");
  expect(satellitePacket.position.cartesianVelocity).toHaveLength(21);
  expect(requests[1]?.sceneSummary?.entities).toContainEqual(
    expect.objectContaining({
      id: satellitePacket.id,
      type: "satellite",
    }),
  );
  expect(requests[1]?.relevantPackets).toContainEqual(satellitePacket);
  const advancedDiagnostics = await readSceneDiagnostics(page);
  expect(advancedDiagnostics.entities[0]?.positionAtCurrentTime).not.toEqual(
    firstPosition,
  );
  expect(Date.parse(advancedDiagnostics.clock!.currentTime)).toBeGreaterThan(
    Date.parse(firstDiagnostics.clock!.currentTime),
  );
  expect(browserErrors).toEqual([]);
});

test("camera focus/track/adjust/orbit and style keep ISS position", async ({
  page,
}) => {
  test.setTimeout(120_000);
  const { browserErrors } = await openApp(page, (request) => {
    if (request.message === "建立验收地面站与国际空间站") {
      return response("已建立地面站与国际空间站。", [
        { op: "upsert", packets: [facilityPacket, issPacket] },
      ]);
    }
    if (request.message === "定位到地面站") {
      return response("已定位到地面站。", [
        {
          op: "camera",
          action: "focus",
          targetId: facilityPacket.id,
          distanceMeters: 2_000_000,
          headingDegrees: 20,
          pitchDegrees: -35,
        },
      ]);
    }
    if (request.message === "跟随国际空间站") {
      return response("已跟随国际空间站。", [
        { op: "camera", action: "track", targetId: issPacket.id },
      ]);
    }
    if (request.message === "再拉近一点并向左转") {
      return response("已相对微调相机。", [
        { op: "camera", action: "zoom", amount: 400_000 },
        {
          op: "camera",
          action: "rotate",
          // 负 headingDegrees = 左转（正右负左）。
          headingDegrees: -30,
        },
      ]);
    }
    if (request.message === "绕国际空间站转一点") {
      return response("已单次环绕国际空间站。", [
        {
          op: "camera",
          action: "orbitStep",
          targetId: issPacket.id,
          amount: 45,
          pitchDegrees: -40,
          distanceMeters: 3_000_000,
        },
      ]);
    }
    if (request.message === "停止跟随并持续环绕") {
      return response("已停止跟随并开始持续环绕。", [
        { op: "camera", action: "untrack" },
        {
          op: "camera",
          action: "orbitStart",
          targetId: issPacket.id,
          angularSpeedDegreesPerSecond: 12,
          pitchDegrees: -35,
          distanceMeters: 3_000_000,
        },
      ]);
    }
    if (request.message === "停止环绕") {
      return response("已停止环绕。", [
        { op: "camera", action: "orbitStop" },
      ]);
    }
    if (request.message === "把国际空间站改成红色，轨迹宽度 5") {
      return response("已更新国际空间站样式。", [
        {
          op: "style",
          id: issPacket.id,
          patch: {
            point: { color: { rgba: [255, 0, 0, 255] }, pixelSize: 12 },
            path: { width: 5 },
          },
        },
      ]);
    }
    return response("场景已就绪。");
  });

  await sendCommand(
    page,
    "建立验收地面站与国际空间站",
    "已建立地面站与国际空间站。",
  );
  const seeded = await readSceneDiagnostics(page);
  expect(seeded.entities).toEqual(
    expect.arrayContaining([
      expect.objectContaining({
        id: facilityPacket.id,
        hasPositionAtCurrentTime: true,
      }),
      expect.objectContaining({
        id: issPacket.id,
        hasPosition: true,
        hasCanonicalPosition: true,
        hasPath: true,
        pathWidth: 2,
      }),
    ]),
  );
  const issBeforeStyle = seeded.entities.find(
    (entity) => entity.id === issPacket.id,
  );
  expect(issBeforeStyle?.positionAtCurrentTime).toHaveLength(3);
  expect(issBeforeStyle?.canonicalPositionSampleCount).toBe(
    issPacket.position.cartesianVelocity.length,
  );

  const beforeFocus = await readSceneDiagnostics(page);
  await sendCommand(page, "定位到地面站", "已定位到地面站。");
  const afterFocus = await readSceneDiagnostics(page);
  expect(afterFocus.camera?.positionWC).not.toBeNull();
  expect(afterFocus.camera?.positionWC).not.toEqual(
    beforeFocus.camera?.positionWC,
  );

  await sendCommand(page, "跟随国际空间站", "已跟随国际空间站。");
  const afterTrack = await readSceneDiagnostics(page);
  expect(afterTrack.camera?.trackedEntityId).toBe(issPacket.id);
  expect(afterTrack.camera?.orbitActive).toBe(false);

  const beforeAdjust = await readLiveSceneDiagnostics(page);
  const headingBeforeLeft = beforeAdjust.camera?.headingDegrees;
  expect(typeof headingBeforeLeft).toBe("number");
  await sendCommand(page, "再拉近一点并向左转", "已相对微调相机。");
  const afterAdjust = await readLiveSceneDiagnostics(page);
  expect(afterAdjust.camera?.trackedEntityId).toBe(issPacket.id);
  expect(afterAdjust.camera?.positionWC).not.toEqual(
    beforeAdjust.camera?.positionWC,
  );
  const headingAfterLeft = afterAdjust.camera?.headingDegrees;
  expect(typeof headingAfterLeft).toBe("number");
  // 左转：heading 相对减小（归一化到 (-180, 180]）。
  const headingDelta =
    ((((headingAfterLeft! - headingBeforeLeft!) % 360) + 540) % 360) - 180;
  expect(headingDelta).toBeLessThan(-5);

  const beforeOrbitStep = afterAdjust.camera?.positionWC;
  await sendCommand(page, "绕国际空间站转一点", "已单次环绕国际空间站。");
  const afterOrbitStep = await readSceneDiagnostics(page);
  expect(afterOrbitStep.camera?.positionWC).not.toEqual(beforeOrbitStep);

  await sendCommand(
    page,
    "停止跟随并持续环绕",
    "已停止跟随并开始持续环绕。",
  );
  const afterOrbitStart = await readSceneDiagnostics(page);
  expect(afterOrbitStart.camera?.trackedEntityId).toBeNull();
  expect(afterOrbitStart.camera?.orbitActive).toBe(true);
  expect(afterOrbitStart.camera?.orbitTargetId).toBe(issPacket.id);

  // 持续环绕依赖时钟推进；用只读 heading 诊断观测环绕推进。
  const headingAtStart =
    (await readLiveSceneDiagnostics(page)).camera?.orbitHeadingDegrees ?? 0;
  const playForward = page.locator(
    'g.cesium-animation-rectButton:has(title:text-is("Play Forward"))',
  );
  await expect(playForward).toBeVisible();
  await playForward.click();
  await expect
    .poll(
      async () => {
        const live = await readLiveSceneDiagnostics(page);
        const heading = live.camera?.orbitHeadingDegrees;
        return {
          orbitActive: live.camera?.orbitActive === true,
          headingAdvanced:
            typeof heading === "number" &&
            Math.abs(heading - headingAtStart) > 1,
          positionMoved:
            JSON.stringify(live.camera?.positionWC) !==
            JSON.stringify(afterOrbitStart.camera?.positionWC),
        };
      },
      { timeout: 15_000 },
    )
    .toEqual({
      orbitActive: true,
      headingAdvanced: true,
      positionMoved: true,
    });

  const pause = page.locator(
    'g.cesium-animation-rectButton:has(title:text-is("Pause"))',
  );
  await pause.click();

  await sendCommand(page, "停止环绕", "已停止环绕。");
  const afterOrbitStop = await readSceneDiagnostics(page);
  expect(afterOrbitStop.camera?.orbitActive).toBe(false);
  expect(afterOrbitStop.camera?.orbitTargetId).toBeNull();

  // 停止后恢复时钟并等待多个 tick：生产相机 position/heading 不得再因 orbit 更新。
  const baseline = await readLiveSceneDiagnostics(page);
  const baselinePosition = baseline.camera?.positionWC;
  const baselineHeading = baseline.camera?.headingDegrees;
  expect(baselinePosition).not.toBeNull();
  expect(typeof baselineHeading).toBe("number");
  await playForward.click();
  await page.waitForTimeout(800);
  const afterTicks = await readLiveSceneDiagnostics(page);
  expect(afterTicks.camera?.orbitActive).toBe(false);
  expect(afterTicks.camera?.positionWC).not.toBeNull();
  expect(typeof afterTicks.camera?.headingDegrees).toBe("number");
  const positionDrift = Math.hypot(
    (afterTicks.camera!.positionWC![0] - baselinePosition![0]),
    (afterTicks.camera!.positionWC![1] - baselinePosition![1]),
    (afterTicks.camera!.positionWC![2] - baselinePosition![2]),
  );
  const headingDrift = Math.abs(
    ((((afterTicks.camera!.headingDegrees! - baselineHeading!) % 360) + 540) %
      360) -
      180,
  );
  // 若 orbit tick listener 未解除，卫星运动会使 lookAt 持续更新，漂移会显著增大。
  expect(positionDrift).toBeLessThan(1);
  expect(headingDrift).toBeLessThan(0.05);
  await pause.click();

  await sendCommand(
    page,
    "把国际空间站改成红色，轨迹宽度 5",
    "已更新国际空间站样式。",
  );
  const afterStyle = await readSceneDiagnostics(page);
  const styledIss = afterStyle.entities.find(
    (entity) => entity.id === issPacket.id,
  );
  expect(styledIss).toEqual(
    expect.objectContaining({
      id: issPacket.id,
      hasPosition: true,
      hasPositionAtCurrentTime: true,
      hasCanonicalPosition: true,
      hasPath: true,
      pathWidth: 5,
      pointColorRgba: [255, 0, 0, 255],
      // 样式合并后完整 Position 采样仍保留（不要求时钟时刻坐标相同）。
      canonicalPositionSampleCount:
        issPacket.position.cartesianVelocity.length,
    }),
  );
  expect(styledIss?.positionAtCurrentTime).toHaveLength(3);

  expect(browserErrors).toEqual([]);
});
