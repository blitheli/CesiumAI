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
  entities: Array<{
    id: string;
    hasPosition: boolean;
    hasPositionAtCurrentTime: boolean;
    hasPoint: boolean;
    hasPath: boolean;
    positionAtCurrentTime?: [number, number, number];
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
