# CesiumAI MVP Implementation Plan

> **Note (2026-07-16):** Skills 接入已从 F3 升级为 F1 submodule。见 `Docs/superpowers/specs/2026-07-16-astrox-skills-submodule-design.md` 与 `Docs/superpowers/plans/2026-07-16-astrox-skills-submodule.md`。下文中 `backend/skills` 手动复制步骤已过时。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a React/Cesium product page where natural-language commands call an ASP.NET Core Microsoft Agent Framework backend and apply typed CZML scene operations for clearing scenes, editing facilities, and visualizing a one-day 900 km SSO/J2 satellite trajectory.

**Architecture:** The React frontend owns the canonical CZML document and applies ordered `clear`, `upsert`, and `delete` operations through one long-lived `CzmlDataSource`. The ASP.NET Core backend owns conversation sessions but not scene state; one agent runtime per session calls strongly typed scene tools, while an Astrox adapter calls `/OrbitWizard/SSO` and `/Propagator/J2`. Model text and collected scene operations are returned together by synchronous `POST /api/chat`.

**Tech Stack:** .NET 10, ASP.NET Core, C# 14, Microsoft Agent Framework, OpenAI-compatible Chat Completions, xUnit, React, TypeScript, Vite, CesiumJS, Vitest, React Testing Library, Playwright.

## Global Constraints

- The frontend is the only authority for the complete CZML scene document.
- Every chat request sends `sceneSummary`; it sends full `relevantPackets` only for selected or name/id-matched entities.
- The API response shape is `{ sessionId, message, sceneOps }`.
- The LLM never authors executable CZML; C# scene tools create all scene-changing packets.
- MVP scene operations are `clear`, `upsert`, and `delete`, executed in response order.
- MVP commands cover scene clearing, facility add/update/delete, and a 900 km SSO propagated for one day with J2.
- Chat uses synchronous `POST /api/chat`; SSE and WebSocket are excluded.
- Astrox skills come from `https://gitee.com/blitheli/astrox-skills.git` and are placed manually under `backend/skills`.
- `AddSatelliteJ2` directly implements the published skill contracts: `POST /OrbitWizard/SSO`, then `POST /Propagator/J2`. Generic HTTP tools remain available for non-scene questions but cannot write `sceneOps`.
- Use .NET 10 and a currently supported Node.js LTS release (Node.js 22 or newer).
- Add dependencies through `dotnet add package` and `npm install` without invented version pins so the package manager resolves the latest available stable release.
- API keys must come from User Secrets or environment variables and must never be committed.
- All automated tests must avoid live LLM and Astrox calls; use fakes or in-memory HTTP handlers.

---

## File Map

### Root and documentation

- `CesiumAI.slnx` — solution containing API and test projects.
- `.gitignore` — excludes generated frontend/backend output, secrets, and the manually cloned `backend/skills`.
- `README.md` — local setup, configuration, skills placement, and run/test commands.
- `Docs/prd.md` — approved product and technical specification; no implementation details are moved out of it.

### Backend

- `backend/CesiumAI.Api/Program.cs` — composition root, CORS, options, typed clients, services, and controllers.
- `backend/CesiumAI.Api/Models/ChatContracts.cs` — request/response, scene summary, and entity summary records.
- `backend/CesiumAI.Api/Models/SceneOps.cs` — polymorphic `SceneOp` records and JSON discriminators.
- `backend/CesiumAI.Api/Controllers/ChatController.cs` — synchronous `POST /api/chat` and timeout/error mapping.
- `backend/CesiumAI.Api/Services/ChatService.cs` — prompt construction, agent turn invocation, and response assembly.
- `backend/CesiumAI.Api/Services/SceneOpCollector.cs` — per-turn operation sink.
- `backend/CesiumAI.Api/Services/AgentRuntimeStore.cs` — one serialized `AIAgent`/`AgentSession` runtime per `sessionId`.
- `backend/CesiumAI.Api/Services/AgentFactory.cs` — OpenAI-compatible agent construction, skills provider, and tools.
- `backend/CesiumAI.Api/Services/AgentInstructions.cs` — immutable system instructions for scene-tool policy.
- `backend/CesiumAI.Api/Services/ScenePromptBuilder.cs` — deterministic scene-context prompt serialization.
- `backend/CesiumAI.Api/Tools/SceneTools.cs` — clear, facility, delete, and satellite tool functions.
- `backend/CesiumAI.Api/Tools/AstroxRawTools.cs` — constrained generic GET/POST tools for skill-guided read/analysis.
- `backend/CesiumAI.Api/Astrox/AstroxClient.cs` — typed SSO and J2 HTTP calls.
- `backend/CesiumAI.Api/Astrox/AstroxContracts.cs` — external request/response DTOs matching astrox-skills.
- `backend/CesiumAI.Api/Astrox/OrbitScenarioService.cs` — SSO/J2 orchestration and complete satellite CZML packet assembly.
- `backend/CesiumAI.Api/Configuration/AgentOptions.cs` — endpoint, key, and model validation.
- `backend/CesiumAI.Api/Configuration/AstroxOptions.cs` — Astrox base URL and default propagation settings.
- `backend/CesiumAI.Api/Configuration/SkillsOptions.cs` — skills path resolved relative to the API content root.
- `backend/CesiumAI.Api/appsettings.json` — non-secret defaults.
- `backend/CesiumAI.Api.Tests/` — xUnit tests mirroring backend units and HTTP integration.

### Frontend

- `frontend/vite.config.ts` — Cesium static asset copying and `CESIUM_BASE_URL`.
- `frontend/src/contracts/chat.ts` — TypeScript API and CZML packet contracts.
- `frontend/src/scene/emptyDocument.ts` — deterministic minimal CZML document factory.
- `frontend/src/scene/sceneDocument.ts` — pure canonical-document reducer and packet selectors.
- `frontend/src/scene/summary.ts` — `SceneSummary` creation and relevant-id inference.
- `frontend/src/scene/CesiumSceneManager.ts` — one `CzmlDataSource`, operation application, selection, and summary façade.
- `frontend/src/components/ViewerHost.tsx` — Cesium Viewer lifecycle.
- `frontend/src/components/ChatPanel.tsx` — message list, input, loading, and errors.
- `frontend/src/api/chat.ts` — typed `fetch` client.
- `frontend/src/app/App.tsx` — Viewer/chat layout and request orchestration.
- `frontend/src/styles.css` — full-height product layout and responsive behavior.
- `frontend/src/**/*.test.ts(x)` — Vitest unit/component tests.
- `frontend/e2e/scene-chat.spec.ts` — Playwright browser acceptance tests with API interception.

---

### Task 1: Scaffold the testable monorepo

**Files:**
- Create: `CesiumAI.slnx`
- Create: `.gitignore`
- Create: `backend/CesiumAI.Api/CesiumAI.Api.csproj`
- Create: `backend/CesiumAI.Api.Tests/CesiumAI.Api.Tests.csproj`
- Create: `frontend/package.json`
- Create: `frontend/vite.config.ts`
- Create: `frontend/src/test/setup.ts`
- Create: `frontend/src/app/App.test.tsx`
- Modify: generated `backend/CesiumAI.Api/Program.cs`
- Create: `frontend/src/app/App.tsx`
- Modify: generated `frontend/src/main.tsx`

**Interfaces:**
- Produces: runnable `dotnet test CesiumAI.slnx` and `npm test -- --run` commands.
- Produces: frontend dev server on port 5173 and backend HTTP server on the configured ASP.NET port.

- [ ] **Step 1: Create backend solution and projects**

Run:

```bash
dotnet new sln --format slnx -n CesiumAI
dotnet new webapi -f net10.0 -n CesiumAI.Api -o backend/CesiumAI.Api --no-openapi
dotnet new xunit -f net10.0 -n CesiumAI.Api.Tests -o backend/CesiumAI.Api.Tests
dotnet sln CesiumAI.slnx add backend/CesiumAI.Api/CesiumAI.Api.csproj
dotnet sln CesiumAI.slnx add backend/CesiumAI.Api.Tests/CesiumAI.Api.Tests.csproj
dotnet add backend/CesiumAI.Api.Tests/CesiumAI.Api.Tests.csproj reference backend/CesiumAI.Api/CesiumAI.Api.csproj
dotnet add backend/CesiumAI.Api/CesiumAI.Api.csproj package Microsoft.Agents.AI.OpenAI
dotnet add backend/CesiumAI.Api.Tests/CesiumAI.Api.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
dotnet add backend/CesiumAI.Api.Tests/CesiumAI.Api.Tests.csproj package FluentAssertions
```

Expected: both projects are listed by `dotnet sln CesiumAI.slnx list`.

- [ ] **Step 2: Create frontend and install runtime/test dependencies**

Run:

```bash
npm create vite@latest frontend -- --template react-ts
cd frontend
npm install
npm install cesium
npm install -D vitest jsdom @testing-library/react @testing-library/jest-dom @testing-library/user-event vite-plugin-static-copy
```

Expected: `npm run build` compiles the generated React application.

- [ ] **Step 3: Configure Vitest and Cesium assets**

Set `frontend/vite.config.ts` to:

```ts
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import { viteStaticCopy } from "vite-plugin-static-copy";

export default defineConfig({
  plugins: [
    react(),
    viteStaticCopy({
      targets: ["Assets", "ThirdParty", "Widgets", "Workers"].map((name) => ({
        src: `node_modules/cesium/Build/Cesium/${name}`,
        dest: "cesium",
      })),
    }),
  ],
  define: {
    CESIUM_BASE_URL: JSON.stringify("/cesium"),
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    css: true,
  },
});
```

Create `frontend/src/test/setup.ts`:

```ts
import "@testing-library/jest-dom/vitest";
```

Merge these scripts into the generated `frontend/package.json` without
removing its `dev`, `build`, `lint`, or `preview` scripts:

```json
{
  "scripts": {
    "test": "vitest",
    "test:coverage": "vitest run --coverage",
    "e2e": "playwright test"
  }
}
```

- [ ] **Step 4: Write and run baseline smoke tests**

Create `frontend/src/app/App.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { App } from "./App";

it("renders the CesiumAI shell", () => {
  render(<App />);
  expect(screen.getByRole("main", { name: "CesiumAI" })).toBeInTheDocument();
});
```

Replace the generated app temporarily with:

```tsx
export function App() {
  return <main aria-label="CesiumAI">CesiumAI</main>;
}
```

Run:

```bash
dotnet test CesiumAI.slnx
cd frontend && npm test -- --run && npm run build
```

Expected: all baseline tests pass and the production bundle is generated.

- [ ] **Step 5: Add generated-output and skills exclusions**

Add to `.gitignore`:

```gitignore
**/bin/
**/obj/
frontend/node_modules/
frontend/dist/
frontend/playwright-report/
frontend/test-results/
backend/skills/
*.user
appsettings.Development.json
```

- [ ] **Step 6: Commit**

```bash
git add .gitignore CesiumAI.slnx backend frontend
git commit -m "build: scaffold CesiumAI frontend and API"
```

---

### Task 2: Define one stable chat and SceneOp contract

**Files:**
- Create: `backend/CesiumAI.Api/Models/ChatContracts.cs`
- Create: `backend/CesiumAI.Api/Models/SceneOps.cs`
- Create: `backend/CesiumAI.Api.Tests/Models/SceneOpSerializationTests.cs`
- Create: `frontend/src/contracts/chat.ts`
- Create: `frontend/src/contracts/chat.test.ts`

**Interfaces:**
- Produces: `ChatRequest`, `ChatResponse`, `SceneSummary`, `EntitySummary`, and polymorphic `SceneOp`.
- Produces: TypeScript `CzmlPacket = { id: string } & Record<string, unknown>`.
- JSON discriminator: exact lowercase property `op` with values `clear`, `upsert`, or `delete`.

- [ ] **Step 1: Write failing backend serialization tests**

Create `SceneOpSerializationTests.cs` with tests that serialize:

```csharp
SceneOp[] operations =
[
    new ClearSceneOp(),
    new UpsertSceneOp([JsonSerializer.SerializeToElement(new { id = "sanya" })]),
    new DeleteSceneOp(["obsolete"])
];

string json = JsonSerializer.Serialize(
    operations,
    new JsonSerializerOptions(JsonSerializerDefaults.Web));

json.Should().Contain("\"op\":\"clear\"");
json.Should().Contain("\"op\":\"upsert\"");
json.Should().Contain("\"op\":\"delete\"");
json.Should().Contain("\"packets\"");
json.Should().Contain("\"ids\"");
```

Run:

```bash
dotnet test CesiumAI.slnx --filter SceneOpSerializationTests
```

Expected: compilation fails because the model types do not exist.

- [ ] **Step 2: Implement backend contracts**

Define `SceneOps.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CesiumAI.Api.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(ClearSceneOp), "clear")]
[JsonDerivedType(typeof(UpsertSceneOp), "upsert")]
[JsonDerivedType(typeof(DeleteSceneOp), "delete")]
public abstract record SceneOp;

public sealed record ClearSceneOp : SceneOp;
public sealed record UpsertSceneOp(IReadOnlyList<JsonElement> Packets) : SceneOp;
public sealed record DeleteSceneOp(IReadOnlyList<string> Ids) : SceneOp;
```

Define `ChatContracts.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace CesiumAI.Api.Models;

public sealed record ChatRequest(
    [property: Required, MinLength(1)] string Message,
    string? SessionId,
    [property: Required] SceneSummary SceneSummary,
    IReadOnlyList<JsonElement>? RelevantPackets);

public sealed record ChatResponse(
    string SessionId,
    string Message,
    IReadOnlyList<SceneOp> SceneOps);

public sealed record SceneSummary(
    DocumentClockSummary? DocumentClock,
    IReadOnlyList<EntitySummary> Entities);

public sealed record DocumentClockSummary(string? Interval, string? CurrentTime);

public sealed record EntitySummary(
    string Id,
    string? Name,
    string Type,
    double? Lon,
    double? Lat,
    double? Alt,
    string? OrbitHint);
```

Run the backend test again. Expected: PASS.

- [ ] **Step 3: Write and implement frontend contract checks**

Create `frontend/src/contracts/chat.ts`:

```ts
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
```

Create `frontend/src/contracts/chat.test.ts`:

```ts
import type { ChatResponse } from "./chat";

it("accepts the wire-level SceneOp union", () => {
  const response = {
    sessionId: "s1",
    message: "done",
    sceneOps: [
      { op: "clear" },
      { op: "upsert", packets: [{ id: "sanya" }] },
      { op: "delete", ids: ["old"] },
    ],
  } satisfies ChatResponse;

  expect(response.sceneOps.map((op) => op.op)).toEqual([
    "clear",
    "upsert",
    "delete",
  ]);
});
```

Run:

```bash
cd frontend && npm test -- --run src/contracts/chat.test.ts
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/CesiumAI.Api/Models backend/CesiumAI.Api.Tests/Models frontend/src/contracts
git commit -m "feat: define chat and scene operation contracts"
```

---

### Task 3: Implement the canonical frontend CZML document

**Files:**
- Create: `frontend/src/scene/emptyDocument.ts`
- Create: `frontend/src/scene/sceneDocument.ts`
- Create: `frontend/src/scene/summary.ts`
- Create: `frontend/src/scene/sceneDocument.test.ts`
- Create: `frontend/src/scene/summary.test.ts`

**Interfaces:**
- Consumes: `CzmlPacket`, `SceneOp`, and `SceneSummary` from Task 2.
- Produces: `createEmptyDocument(now: Date): CzmlPacket[]`.
- Produces: `reduceSceneDocument(current, operations, emptyDocument): CzmlPacket[]`.
- Produces: `buildSceneSummary(document): SceneSummary`.
- Produces: `pickRelevantPackets(document, ids): CzmlPacket[]`.
- Produces: `inferRelevantEntityIds(text, summary, selectedIds): string[]`.

- [ ] **Step 1: Write failing reducer tests**

Cover these exact cases:

```ts
it("clears, replaces complete packets by id, and deletes in order", () => {
  const empty = createEmptyDocument(new Date("2026-07-16T00:00:00Z"));
  const current = [
    ...empty,
    { id: "sanya", name: "old", point: { pixelSize: 4 } },
    { id: "remove-me", point: {} },
  ];
  const result = reduceSceneDocument(
    current,
    [
      { op: "upsert", packets: [{ id: "sanya", name: "new", point: { pixelSize: 10 } }] },
      { op: "delete", ids: ["remove-me"] },
    ],
    empty,
  );

  expect(result.find((packet) => packet.id === "sanya")).toEqual({
    id: "sanya",
    name: "new",
    point: { pixelSize: 10 },
  });
  expect(result.some((packet) => packet.id === "remove-me")).toBe(false);
});
```

Also assert that a `clear` operation discards every business entity and that caller-owned arrays are not mutated.

Run:

```bash
cd frontend && npm test -- --run src/scene/sceneDocument.test.ts
```

Expected: FAIL because the scene modules do not exist.

- [ ] **Step 2: Implement empty document and pure reducer**

`createEmptyDocument` must create:

```ts
{
  id: "document",
  name: "CesiumAI Scene",
  version: "1.0",
  clock: {
    interval: `${startIso}/${stopIso}`,
    currentTime: startIso,
    multiplier: 60
  }
}
```

where `startIso` is `now.toISOString()` and `stopIso` is exactly 24 hours later.

Reducer rules:

1. Copy all packets before processing.
2. `clear` replaces the working array with a copy of `emptyDocument`.
3. `upsert` rejects `document` packets and replaces the complete existing packet with the same id; otherwise it appends.
4. `delete` ignores id `document` and removes matching business entities.
5. Return a new array and copied packet objects.

Run the reducer tests. Expected: PASS.

- [ ] **Step 3: Write failing summary and relevance tests**

Test:

- `{ point, position.cartographicDegrees: [109.5, 18.2, 50] }` becomes a `facility`.
- `{ path, position.cartesianVelocity: [...] }` becomes a `satellite`.
- document packets are excluded.
- selected ids come first, then case-insensitive exact id/name substring matches, with duplicates removed.
- `pickRelevantPackets` returns cloned full packets only for requested ids.

Run:

```bash
cd frontend && npm test -- --run src/scene/summary.test.ts
```

Expected: FAIL because summary functions do not exist.

- [ ] **Step 4: Implement summary and relevance functions**

Use these classification rules:

```ts
const type =
  "path" in packet ? "satellite" :
  "point" in packet ? "facility" :
  "other";
```

For a static facility, read the first three numeric values from
`position.cartographicDegrees`. Set satellite `orbitHint` from
`properties.orbitHint.string` when present, otherwise from its name.

Run all frontend scene tests. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/scene
git commit -m "feat: add canonical CZML scene document"
```

---

### Task 4: Build the typed Astrox SSO/J2 pipeline

**Files:**
- Create: `backend/CesiumAI.Api/Configuration/AstroxOptions.cs`
- Create: `backend/CesiumAI.Api/Astrox/AstroxContracts.cs`
- Create: `backend/CesiumAI.Api/Astrox/AstroxClient.cs`
- Create: `backend/CesiumAI.Api/Astrox/OrbitScenarioService.cs`
- Create: `backend/CesiumAI.Api.Tests/Astrox/AstroxClientTests.cs`
- Create: `backend/CesiumAI.Api.Tests/Astrox/OrbitScenarioServiceTests.cs`
- Create: `backend/CesiumAI.Api.Tests/TestSupport/StubHttpMessageHandler.cs`

**Interfaces:**
- Produces: `IAstroxClient.CreateSsoAsync(SsoRequest, CancellationToken)`.
- Produces: `IAstroxClient.PropagateJ2Async(J2Request, CancellationToken)`.
- Produces: `IOrbitScenarioService.CreateSsoJ2PacketAsync(SsoJ2Scenario, CancellationToken): Task<JsonElement>`.
- External endpoint contracts are `/OrbitWizard/SSO` and `/Propagator/J2`.

- [ ] **Step 1: Write failing HTTP contract tests**

The SSO test must capture and assert this request:

```json
{
  "Description": "SSO-900",
  "OrbitEpoch": "2026-07-16T00:00:00.000Z",
  "Altitude": 900,
  "LocalTimeOfDescendingNode": 10.5
}
```

at `POST /OrbitWizard/SSO`.

The J2 test must capture and assert:

```json
{
  "Start": "2026-07-16T00:00:00.000Z",
  "Stop": "2026-07-17T00:00:00.000Z",
  "CentralBody": "Earth",
  "OrbitEpoch": "2026-07-16T00:00:00.000Z",
  "CoordType": "Classical",
  "OrbitalElements": [7278136.3, 0.001, 98.9, 0, 0, 0],
  "Step": 60
}
```

at `POST /Propagator/J2`.

Return in-memory responses containing `IsSuccess`, `Message`, SSO
`Elements_Inertial`, and J2 `Position.cartesianVelocity`.

Run:

```bash
dotnet test CesiumAI.slnx --filter "AstroxClientTests"
```

Expected: compilation fails because the Astrox types do not exist.

- [ ] **Step 2: Implement options and exact external DTOs**

Define:

```csharp
public sealed class AstroxOptions
{
    public const string SectionName = "Astrox";
    public required Uri BaseUrl { get; init; }
    public int DefaultStepSeconds { get; init; } = 60;
    public double DefaultDescendingNodeLocalTime { get; init; } = 10.5;
}
```

Define records for:

- `SsoRequest(Description, OrbitEpoch, Altitude, LocalTimeOfDescendingNode)`.
- `SsoResponse(IsSuccess, Message, ElementsInertial)` with
  `[JsonPropertyName("Elements_Inertial")]`.
- `OrbitalElements(SemimajorAxis, Eccentricity, Inclination, ArgumentOfPeriapsis, RightAscensionOfAscendingNode, TrueAnomaly, GravitationalParameter)`.
- `J2Request(Start, Stop, CentralBody, OrbitEpoch, CoordType, OrbitalElements, Step)`.
- `J2Response(IsSuccess, Message, JsonElement Position, double Period)`.
- `SsoJ2Scenario(Id, Name, AltitudeKm, EpochUtc, Hours, StepSeconds, LocalTimeOfDescendingNode)`.
- `AstroxException : Exception` for HTTP/body failures.

Use PascalCase JSON naming for outgoing requests because the published
astrox-skills contracts use PascalCase.

- [ ] **Step 3: Implement AstroxClient**

For each method:

1. use `PostAsJsonAsync`;
2. call `EnsureSuccessStatusCode`;
3. deserialize case-insensitively;
4. throw `AstroxException` if the body is absent or `IsSuccess` is false;
5. include the endpoint and server `Message` in the exception.

Run `AstroxClientTests`. Expected: PASS.

- [ ] **Step 4: Write failing orchestration test**

Given:

```csharp
var scenario = new SsoJ2Scenario(
    Id: "sso-900",
    Name: "SSO 900 km",
    AltitudeKm: 900,
    EpochUtc: DateTimeOffset.Parse("2026-07-16T00:00:00Z"),
    Hours: 24,
    StepSeconds: 60,
    LocalTimeOfDescendingNode: 10.5);
```

assert that:

- SSO is called before J2;
- J2 receives SSO `Elements_Inertial` in this exact order:
  semimajor axis, eccentricity, inclination, argument of periapsis,
  right ascension of ascending node, true anomaly;
- returned CZML has `id`, `name`, `availability`, `position`, `point`, `path`,
  and `properties.orbitHint.string == "900 km SSO / J2"`;
- `position` is copied from the successful J2 response;
- no packet is returned when either Astrox call fails.

Run:

```bash
dotnet test CesiumAI.slnx --filter "OrbitScenarioServiceTests"
```

Expected: FAIL because `OrbitScenarioService` does not exist.

- [ ] **Step 5: Implement OrbitScenarioService**

Validation:

- id and name are non-empty;
- altitude is in `[100, 100000]` km;
- hours is in `(0, 24]`;
- step is in `[1, 3600]` seconds;
- local descending-node time is in `[0, 24)`.

Use ISO 8601 UTC strings with millisecond precision. Build a complete packet:

```json
{
  "id": "sso-900",
  "name": "SSO 900 km",
  "availability": "2026-07-16T00:00:00.000Z/2026-07-17T00:00:00.000Z",
  "position": {},
  "point": {
    "pixelSize": 8,
    "color": { "rgba": [255, 220, 0, 255] }
  },
  "path": {
    "show": true,
    "width": 2,
    "leadTime": 0,
    "trailTime": 86400,
    "material": {
      "solidColor": { "color": { "rgba": [0, 200, 255, 220] } }
    }
  },
  "properties": {
    "orbitHint": { "string": "900 km SSO / J2" }
  }
}
```

Replace the empty `position` object with the exact J2 `Position` payload.

Run all Astrox tests. Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/CesiumAI.Api/Astrox backend/CesiumAI.Api/Configuration/AstroxOptions.cs backend/CesiumAI.Api.Tests/Astrox backend/CesiumAI.Api.Tests/TestSupport
git commit -m "feat: add typed Astrox SSO and J2 pipeline"
```

---

### Task 5: Add isolated scene tools and operation collection

**Files:**
- Create: `backend/CesiumAI.Api/Services/SceneOpCollector.cs`
- Create: `backend/CesiumAI.Api/Tools/SceneTools.cs`
- Create: `backend/CesiumAI.Api.Tests/Services/SceneOpCollectorTests.cs`
- Create: `backend/CesiumAI.Api.Tests/Tools/SceneToolsTests.cs`

**Interfaces:**
- Consumes: backend SceneOp records from Task 2.
- Consumes: `IOrbitScenarioService` from Task 4.
- Produces: `ISceneOpSink.Add(SceneOp operation)`.
- Produces: `SceneOpCollector.Drain(): IReadOnlyList<SceneOp>`.
- Produces: tool methods `ClearScene`, `UpsertFacility`, `DeleteEntity`, and `AddSatelliteJ2`.

- [ ] **Step 1: Write failing collector tests**

Assert:

```csharp
var collector = new SceneOpCollector();
collector.Add(new ClearSceneOp());

collector.Drain().Should().ContainSingle().Which.Should().BeOfType<ClearSceneOp>();
collector.Drain().Should().BeEmpty();
```

Also start 20 parallel `Add` calls and assert that one `Drain` returns all 20
operations once.

Run:

```bash
dotnet test CesiumAI.slnx --filter SceneOpCollectorTests
```

Expected: compilation fails because the collector does not exist.

- [ ] **Step 2: Implement the collector**

Use a private lock and private list:

```csharp
public interface ISceneOpSink
{
    void Add(SceneOp operation);
}

public sealed class SceneOpCollector : ISceneOpSink
{
    private readonly object _gate = new();
    private List<SceneOp> _operations = [];

    public void Add(SceneOp operation)
    {
        lock (_gate) _operations.Add(operation);
    }

    public IReadOnlyList<SceneOp> Drain()
    {
        lock (_gate)
        {
            SceneOp[] result = [.. _operations];
            _operations = [];
            return result;
        }
    }
}
```

Run collector tests. Expected: PASS.

- [ ] **Step 3: Write failing scene tool tests**

Test exact behavior:

- `ClearScene()` adds one `ClearSceneOp`.
- `UpsertFacility("sanya", "三亚", 109.5, 18.2, 50)` adds one complete
  facility packet with `cartographicDegrees`, point, and label.
- longitude outside `[-180, 180]`, latitude outside `[-90, 90]`, or blank id
  throws `ArgumentOutOfRangeException`/`ArgumentException` without adding ops.
- `DeleteEntity(["a", "a", "document", " "])` adds one delete op with only `a`.
- `AddSatelliteJ2(...)` awaits `IOrbitScenarioService`, then adds its complete
  packet; a thrown Astrox error adds nothing.

Run:

```bash
dotnet test CesiumAI.slnx --filter SceneToolsTests
```

Expected: FAIL because `SceneTools` does not exist.

- [ ] **Step 4: Implement SceneTools**

Use `[Description]` on public tool methods. Return concise strings for the
model, for example `"Facility 'sanya' queued for upsert."`; execution authority
remains the collector.

Use these exact public signatures:

```csharp
string ClearScene();
string UpsertFacility(
    string id,
    string? name,
    double longitudeDegrees,
    double latitudeDegrees,
    double altitudeMeters = 0);
string DeleteEntity(string[] ids);
Task<string> AddSatelliteJ2(
    string id,
    string? name = null,
    double altitudeKm = 900,
    double hours = 24,
    int stepSeconds = 60,
    double localTimeOfDescendingNode = 10.5,
    string? epochUtc = null,
    CancellationToken cancellationToken = default);
```

The facility packet must be:

```json
{
  "id": "sanya",
  "name": "三亚",
  "position": { "cartographicDegrees": [109.5, 18.2, 50] },
  "point": {
    "pixelSize": 10,
    "color": { "rgba": [255, 80, 80, 255] },
    "outlineColor": { "rgba": [255, 255, 255, 255] },
    "outlineWidth": 2
  },
  "label": {
    "text": "三亚",
    "show": true,
    "pixelOffset": { "cartesian2": [0, -18] }
  }
}
```

For `AddSatelliteJ2`, default missing values to 900 km, 24 hours, 60 seconds,
10.5 local descending-node time, and `TimeProvider.GetUtcNow()` rounded down
to the current minute.

Run all Task 5 tests. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/CesiumAI.Api/Services/SceneOpCollector.cs backend/CesiumAI.Api/Tools/SceneTools.cs backend/CesiumAI.Api.Tests/Services backend/CesiumAI.Api.Tests/Tools
git commit -m "feat: add strongly typed scene tools"
```

---

### Task 6: Integrate Microsoft Agent Framework with safe sessions

**Files:**
- Create: `backend/CesiumAI.Api/Configuration/AgentOptions.cs`
- Create: `backend/CesiumAI.Api/Configuration/SkillsOptions.cs`
- Create: `backend/CesiumAI.Api/Services/AgentFactory.cs`
- Create: `backend/CesiumAI.Api/Services/AgentInstructions.cs`
- Create: `backend/CesiumAI.Api/Services/AgentRuntimeStore.cs`
- Create: `backend/CesiumAI.Api/Services/ScenePromptBuilder.cs`
- Create: `backend/CesiumAI.Api/Services/ChatService.cs`
- Create: `backend/CesiumAI.Api/Tools/AstroxRawTools.cs`
- Create: `backend/CesiumAI.Api.Tests/Services/ScenePromptBuilderTests.cs`
- Create: `backend/CesiumAI.Api.Tests/Services/ChatServiceTests.cs`
- Create: `backend/CesiumAI.Api.Tests/Services/AgentRuntimeStoreTests.cs`
- Create: `backend/CesiumAI.Api.Tests/Tools/AstroxRawToolsTests.cs`

**Interfaces:**
- Consumes: chat models, collector, scene tools, Astrox client, and `backend/skills`.
- Produces: `IChatService.ChatAsync(ChatRequest, CancellationToken)`.
- Produces: `IAgentTurnRunner.RunAsync(sessionId, prompt, collector, cancellationToken)`.
- Session invariant: the same session reuses the same `AIAgent` and
  `AgentSession`; concurrent turns for that session are serialized.

- [ ] **Step 1: Write failing prompt-builder tests**

For a request with one summary and one relevant packet, assert exact section
ordering:

```text
[SCENE_SUMMARY]
{...}

[RELEVANT_CZML_PACKETS]
[{...}]

[USER]
把 sanya 高度改为 50 米
```

Use `JsonSerializerOptions.WriteIndented = false` and camelCase. Assert a
request without relevant packets emits `[]`.

Run:

```bash
dotnet test CesiumAI.slnx --filter ScenePromptBuilderTests
```

Expected: FAIL because the builder does not exist.

- [ ] **Step 2: Implement ScenePromptBuilder**

Expose:

```csharp
public interface IScenePromptBuilder
{
    string Build(ChatRequest request);
}
```

Serialize only supplied scene state and the user message. Do not include API
keys, backend configuration, or prior session messages.

Run prompt tests. Expected: PASS.

- [ ] **Step 3: Write failing ChatService tests**

Using a fake `IAgentTurnRunner`, assert:

- blank/absent session id becomes a new GUID;
- supplied session id is preserved;
- the runner receives the built prompt;
- operations returned by `collector.Drain()` appear in `ChatResponse`;
- agent text appears only in `message`;
- an agent exception returns no response and never fabricates operations.

Run:

```bash
dotnet test CesiumAI.slnx --filter ChatServiceTests
```

Expected: FAIL because the service interfaces do not exist.

- [ ] **Step 4: Implement ChatService and runner boundary**

Define:

```csharp
public interface IAgentTurnRunner
{
    Task<string> RunAsync(
        string sessionId,
        string prompt,
        SceneOpCollector collector,
        CancellationToken cancellationToken);
}
```

`ChatService.ChatAsync` creates one collector, awaits one runner turn, drains
once, and returns a `ChatResponse`.

Run ChatService tests. Expected: PASS.

- [ ] **Step 5: Write failing runtime isolation tests**

Create a fake runtime factory whose run method waits on a controllable task.
Assert:

- two turns for the same session never overlap;
- turns for two different sessions may overlap;
- each turn writes only to its own collector;
- the runtime factory is called once per session id.

Run:

```bash
dotnet test CesiumAI.slnx --filter AgentRuntimeStoreTests
```

Expected: FAIL because runtime storage does not exist.

- [ ] **Step 6: Implement per-session agent runtimes**

Store:

```csharp
private readonly ConcurrentDictionary<string, Lazy<Task<AgentRuntime>>> _runtimes = new();
```

Each `AgentRuntime` contains:

- one `AIAgent`;
- one `AgentSession`;
- one `SemaphoreSlim(1, 1)`;
- a `TurnSceneOpSink` captured by the scene-tool delegates.

During a turn:

1. await the runtime semaphore;
2. set `TurnSceneOpSink.Current` to the request collector;
3. call `agent.RunAsync(prompt, session, cancellationToken: token)`;
4. clear `Current` in `finally`;
5. release the semaphore.

`TurnSceneOpSink.Add` must throw when no turn is active. This prevents
operations from escaping their request.

- [ ] **Step 7: Implement AgentFactory**

Use `Microsoft.Agents.AI.OpenAI` with Chat Completions because the configured
Kimi endpoint is OpenAI-compatible:

```csharp
var client = new OpenAIClient(
    new ApiKeyCredential(options.ApiKey),
    new OpenAIClientOptions { Endpoint = options.Endpoint });

AIAgent agent = client.GetChatClient(options.Model).AsAIAgent(
    new ChatClientAgentOptions
    {
        Name = "SpaceAgent",
        Description = "航天任务设计与 Cesium 场景助手",
        ChatOptions = new()
        {
            Instructions = AgentInstructions.Text,
            Tools = tools
        },
        AIContextProviders = [skillsProvider]
    });
```

Create tools with:

```csharp
AIFunctionFactory.Create(sceneTools.ClearScene)
AIFunctionFactory.Create(sceneTools.UpsertFacility)
AIFunctionFactory.Create(sceneTools.DeleteEntity)
AIFunctionFactory.Create(sceneTools.AddSatelliteJ2)
AIFunctionFactory.Create(rawTools.HttpGet)
AIFunctionFactory.Create(rawTools.HttpPost)
```

Instructions must explicitly state:

1. scene mutations require a scene tool;
2. never place executable CZML in assistant text;
3. pure questions do not use scene tools;
4. `AddSatelliteJ2` is the only MVP path for SSO/J2 scene creation;
5. answer users in concise Chinese.

Store this exact policy in `AgentInstructions.Text`; do not duplicate it in
`Program.cs` or `ChatService`.

Create `AgentSkillsProvider` from the configured `Skills:Path`; fail startup
with a clear message when the directory is absent.

Resolve the configured path relative to `IHostEnvironment.ContentRootPath`.
With the repository layout in this plan, the default value is `../skills`,
which resolves to `backend/skills` when the content root is
`backend/CesiumAI.Api`.

- [ ] **Step 8: Test and implement constrained generic HTTP tools**

Tests must reject:

- absolute URLs;
- `..` path traversal;
- paths not beginning with `/`;
- non-Astrox hosts by construction.

Allow relative Astrox paths such as `/ssc?sscName=ISS` and
`/Propagator/TwoBody`. Return status code and body; do not write to a scene
collector.

Run:

```bash
dotnet test CesiumAI.slnx --filter "AgentRuntimeStoreTests|AstroxRawToolsTests|ChatServiceTests|ScenePromptBuilderTests"
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add backend/CesiumAI.Api/Configuration/AgentOptions.cs backend/CesiumAI.Api/Services backend/CesiumAI.Api/Tools/AstroxRawTools.cs backend/CesiumAI.Api.Tests/Services backend/CesiumAI.Api.Tests/Tools/AstroxRawToolsTests.cs
git commit -m "feat: integrate agent sessions and scene operation collection"
```

---

### Task 7: Expose the synchronous chat API

**Files:**
- Create: `backend/CesiumAI.Api/Controllers/ChatController.cs`
- Create: `backend/CesiumAI.Api.Tests/Controllers/ChatControllerTests.cs`
- Create: `backend/CesiumAI.Api.Tests/ApiFactory.cs`
- Modify: `backend/CesiumAI.Api/Program.cs`
- Modify: `backend/CesiumAI.Api/appsettings.json`

**Interfaces:**
- Consumes: `IChatService` from Task 6.
- Produces: `POST /api/chat`.
- Produces: 200 `ChatResponse`, 400 validation responses, 499 client
  cancellation, and 504 server timeout.

- [ ] **Step 1: Write failing HTTP integration tests**

Use `WebApplicationFactory<Program>` and replace `IChatService` with a fake.
Test:

```http
POST /api/chat
Content-Type: application/json

{
  "message": "清空当前场景",
  "sessionId": null,
  "sceneSummary": { "entities": [] },
  "relevantPackets": []
}
```

Expected 200 body:

```json
{
  "sessionId": "test-session",
  "message": "已清空场景。",
  "sceneOps": [{ "op": "clear" }]
}
```

Also assert whitespace `message` returns 400 and a service-side timeout
returns 504 with:

```json
{ "error": "agent_timeout", "detail": "Agent request exceeded 120 seconds." }
```

Run:

```bash
dotnet test CesiumAI.slnx --filter ChatControllerTests
```

Expected: FAIL because the route and composition root are absent.

- [ ] **Step 2: Implement ChatController**

Use `[ApiController]`, `[Route("api/chat")]`, and a linked
`CancellationTokenSource` with `CancelAfter(TimeSpan.FromSeconds(120))`.
Distinguish `HttpContext.RequestAborted` from the internal timeout:

- client cancellation: status 499;
- internal timeout: status 504;
- invalid model state: automatic 400;
- successful result: 200.

- [ ] **Step 3: Wire Program.cs**

Register:

- controllers;
- validated `AgentOptions` and `AstroxOptions`;
- named/typed Astrox `HttpClient`;
- singleton `TimeProvider.System`;
- singleton `IOrbitScenarioService`;
- singleton `IAgentRuntimeStore`/`IAgentTurnRunner`;
- scoped `IChatService`;
- development CORS policy allowing `http://localhost:5173`.

Expose `public partial class Program;` for `WebApplicationFactory`.

Set non-secret defaults in `appsettings.json`:

```json
{
  "Agent": {
    "Endpoint": "https://api.moonshot.cn/v1",
    "Model": "kimi-k2.6"
  },
  "Astrox": {
    "BaseUrl": "http://astrox.cn:8765",
    "DefaultStepSeconds": 60,
    "DefaultDescendingNodeLocalTime": 10.5
  },
  "Skills": {
    "Path": "../skills"
  }
}
```

Read `Agent:ApiKey` only from User Secrets or `Agent__ApiKey`.

- [ ] **Step 4: Run backend suite**

Run:

```bash
dotnet test CesiumAI.slnx
```

Expected: all backend unit and HTTP integration tests pass without network
access.

- [ ] **Step 5: Commit**

```bash
git add backend/CesiumAI.Api/Controllers backend/CesiumAI.Api/Program.cs backend/CesiumAI.Api/appsettings.json backend/CesiumAI.Api.Tests/Controllers backend/CesiumAI.Api.Tests/ApiFactory.cs
git commit -m "feat: expose synchronous agent chat API"
```

---

### Task 8: Integrate the long-lived Cesium Viewer and SceneManager

**Files:**
- Create: `frontend/src/scene/CesiumSceneManager.ts`
- Create: `frontend/src/scene/CesiumSceneManager.test.ts`
- Create: `frontend/src/components/ViewerHost.tsx`
- Create: `frontend/src/components/ViewerHost.test.tsx`
- Modify: `frontend/src/main.tsx`

**Interfaces:**
- Consumes: pure document functions from Task 3.
- Produces: `CesiumSceneManager.initialize(viewer)`.
- Produces: `applySceneOps(operations): Promise<void>`.
- Produces: `buildSummary()`, `pickRelevantPackets(ids)`, and
  `getSelectedEntityIds()`.

- [ ] **Step 1: Write failing manager tests with a fake data source**

Inject a port:

```ts
export interface CzmlDataSourcePort {
  load(packets: CzmlPacket[]): Promise<unknown>;
  process(packets: CzmlPacket[]): Promise<unknown>;
  removeById(id: string): boolean;
  syncViewerClock(): void;
}
```

Assert:

- initialization calls `load` once with the empty document;
- `clear` calls `load`;
- `upsert` calls `process`;
- `delete` calls `removeById`;
- successful `upsert` calls `syncViewerClock` so the Viewer timeline follows
  satellite availability;
- operations execute in array order;
- internal `sceneDocument` changes only after the corresponding Cesium call
  succeeds;
- processing stops at the first rejected operation.

Run:

```bash
cd frontend && npm test -- --run src/scene/CesiumSceneManager.test.ts
```

Expected: FAIL because the manager does not exist.

- [ ] **Step 2: Implement CesiumSceneManager**

Construct it with an empty-document factory and a data-source adapter.
Maintain private `sceneDocument` and selected-id set. Expose cloned values,
never mutable internal arrays.

Production adapter:

```ts
const dataSource = new CzmlDataSource("scene");
await dataSource.load(emptyDocument);
viewer.dataSources.add(dataSource);
```

Call `dataSource.process(packets)` for upserts and
`dataSource.entities.removeById(id)` for deletes.

After a successful upsert, if `dataSource.clock` exists, clone its
`startTime`, `stopTime`, `currentTime`, `clockRange`, and `multiplier` into
`viewer.clock`; then call `viewer.timeline.zoomTo(startTime, stopTime)`.
Implement that behavior behind `syncViewerClock()` so unit tests do not
require WebGL.

Run manager tests. Expected: PASS.

- [ ] **Step 3: Write failing Viewer lifecycle tests**

Mock the Cesium module and assert:

- one Viewer is created on mount;
- manager initialization is called once;
- Viewer is destroyed on unmount;
- rerender does not create a second Viewer.

Run:

```bash
cd frontend && npm test -- --run src/components/ViewerHost.test.tsx
```

Expected: FAIL because the component does not exist.

- [ ] **Step 4: Implement ViewerHost**

Import:

```ts
import "cesium/Build/Cesium/Widgets/widgets.css";
```

Create one Viewer with animation/timeline enabled and code/sandbox widgets
absent. Avoid Cesium ion credentials by starting with `baseLayer: false`,
then add Natural Earth II from the packaged Cesium assets:

```ts
const imagery = await TileMapServiceImageryProvider.fromUrl(
  buildModuleUrl("Assets/Textures/NaturalEarthII"),
);
viewer.imageryLayers.addImageryProvider(imagery);
```

On `viewer.selectedEntityChanged`, update manager selection with zero or one
entity id. Clean up listener and Viewer on unmount.

Run Viewer tests and `npm run build`. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/scene/CesiumSceneManager.ts frontend/src/scene/CesiumSceneManager.test.ts frontend/src/components/ViewerHost.tsx frontend/src/components/ViewerHost.test.tsx frontend/src/main.tsx
git commit -m "feat: integrate Cesium viewer and scene manager"
```

---

### Task 9: Build the chat product flow

**Files:**
- Create: `frontend/src/api/chat.ts`
- Create: `frontend/src/api/chat.test.ts`
- Create: `frontend/src/components/ChatPanel.tsx`
- Create: `frontend/src/components/ChatPanel.test.tsx`
- Modify: `frontend/src/app/App.tsx`
- Modify: `frontend/src/app/App.test.tsx`
- Create: `frontend/src/styles.css`

**Interfaces:**
- Consumes: `ChatRequest`/`ChatResponse`, `CesiumSceneManager`, ViewerHost.
- Produces: `postChat(request, signal): Promise<ChatResponse>`.
- Produces: complete request assembly using A2 summary/relevant-packet rules.

- [ ] **Step 1: Write failing API client tests**

Mock `fetch` and assert:

- POST URL is `${VITE_API_BASE_URL ?? ""}/api/chat`;
- body is JSON and includes all request fields;
- caller abort signal is passed through;
- non-2xx response throws an error containing server `detail`;
- malformed success JSON lacking `sessionId`, `message`, or `sceneOps` throws.

Run:

```bash
cd frontend && npm test -- --run src/api/chat.test.ts
```

Expected: FAIL because the API client does not exist.

- [ ] **Step 2: Implement typed chat client**

Keep runtime validation minimal and explicit:

```ts
function isChatResponse(value: unknown): value is ChatResponse {
  if (!value || typeof value !== "object") return false;
  const result = value as Partial<ChatResponse>;
  return (
    typeof result.sessionId === "string" &&
    typeof result.message === "string" &&
    Array.isArray(result.sceneOps)
  );
}
```

Run API client tests. Expected: PASS.

- [ ] **Step 3: Write failing ChatPanel tests**

Assert:

- submitting a non-empty message invokes `onSend`;
- button/input are disabled while loading;
- blank messages are ignored;
- Enter submits and Shift+Enter adds a newline;
- assistant and user messages are rendered;
- errors use `role="alert"`.

Run:

```bash
cd frontend && npm test -- --run src/components/ChatPanel.test.tsx
```

Expected: FAIL because ChatPanel does not exist.

- [ ] **Step 4: Implement ChatPanel**

Use controlled textarea state and this message type:

```ts
export type UiMessage = {
  id: string;
  role: "user" | "assistant";
  text: string;
};
```

Do not render raw HTML from model responses.

- [ ] **Step 5: Write failing App orchestration tests**

Inject fake chat client and scene manager. For input `"把 sanya 高度改为 50"`:

1. manager returns summary containing `sanya`;
2. selected id and text match infer relevant id;
3. request contains only sanya's full packet;
4. returned `sessionId` is reused on the second request;
5. assistant text is appended;
6. `sceneOps` are applied exactly once;
7. API/apply errors appear without automatic retry.

Run:

```bash
cd frontend && npm test -- --run src/app/App.test.tsx
```

Expected: FAIL until App orchestration is implemented.

- [ ] **Step 6: Implement App and responsive product layout**

App flow:

```ts
const summary = sceneManager.buildSummary();
const ids = inferRelevantEntityIds(text, summary, sceneManager.getSelectedEntityIds());
const relevantPackets = sceneManager.pickRelevantPackets(ids);
const response = await postChat({
  message: text,
  sessionId,
  sceneSummary: summary,
  relevantPackets,
}, abortController.signal);
setSessionId(response.sessionId);
await sceneManager.applySceneOps(response.sceneOps);
```

Layout requirements:

- desktop: Viewer fills remaining width; chat is 380 px wide;
- viewport height: 100%;
- mobile below 800 px: chat becomes a bottom panel no taller than 45%;
- loading state is visible;
- no code editor, run button, iframe, or sandbox UI.

Run:

```bash
cd frontend && npm test -- --run && npm run build
```

Expected: all frontend tests pass and production build succeeds.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/api frontend/src/components/ChatPanel.tsx frontend/src/components/ChatPanel.test.tsx frontend/src/app frontend/src/styles.css
git commit -m "feat: add scene-aware agent chat experience"
```

---

### Task 10: Add acceptance coverage and operator documentation

**Files:**
- Create: `frontend/playwright.config.ts`
- Create: `frontend/e2e/scene-chat.spec.ts`
- Modify: `frontend/package.json`
- Create: `README.md`
- Modify: `Docs/prd.md` only if implementation reveals a factual contract correction.

**Interfaces:**
- Consumes: completed frontend/backend.
- Produces: browser acceptance coverage for the four MVP scene flows.
- Produces: reproducible local setup with astrox-skills and secret configuration.

- [ ] **Step 1: Install and configure Playwright**

Run:

```bash
cd frontend
npm install -D @playwright/test
npx playwright install chromium
```

Configure Playwright to start Vite and use `http://127.0.0.1:5173`.

- [ ] **Step 2: Write browser acceptance tests with intercepted chat API**

Intercept `**/api/chat` and return deterministic responses for:

1. clear → `{ op: "clear" }`;
2. facility add → full `upsert` packet at `[-100, 30.2, 10]`;
3. facility update → same id with a new static position;
4. satellite add → golden `cartesianVelocity` packet with at least three time
   samples and an interval.

Assert:

- requests contain `sceneSummary`;
- a named/selected entity appears in `relevantPackets`;
- assistant messages render;
- no browser console errors occur;
- Cesium canvas remains mounted across all commands.

Run:

```bash
cd frontend && npm run e2e
```

Expected: all four browser scenarios pass.

- [ ] **Step 3: Write README setup and operation instructions**

Document exact commands:

```bash
git clone https://gitee.com/blitheli/astrox-skills.git /tmp/astrox-skills
mkdir -p backend/skills
cp -R /tmp/astrox-skills/skills/. backend/skills/

dotnet user-secrets init --project backend/CesiumAI.Api
dotnet user-secrets set "Agent:ApiKey" "<your-key>" --project backend/CesiumAI.Api

dotnet run --project backend/CesiumAI.Api
cd frontend && npm run dev
```

Also document environment alternatives:

```text
Agent__ApiKey
Agent__Endpoint
Agent__Model
Astrox__BaseUrl
Skills__Path
VITE_API_BASE_URL
```

Include test commands and the expected four manual prompts.

- [ ] **Step 4: Run complete verification**

Run:

```bash
dotnet test CesiumAI.slnx
cd frontend && npm test -- --run
cd frontend && npm run build
cd frontend && npm run e2e
```

Expected: every command exits 0, with no live LLM/Astrox dependency in
automated tests.

- [ ] **Step 5: Perform one opt-in live smoke test when credentials and Astrox are reachable**

Start both services and submit:

```text
清空当前场景
添加一个地面站，经纬高是 -100, 30.2, 10
把该地面站高度改为 50 米
添加一个 900km SSO 卫星，使用 J2 递推一天
```

Verify the API returns typed `sceneOps`, the frontend never extracts CZML
from assistant text, and Cesium animates the satellite. If credentials are
not configured, record the automated verification result and skip only this
live smoke test.

- [ ] **Step 6: Commit**

```bash
git add frontend/playwright.config.ts frontend/e2e frontend/package.json frontend/package-lock.json README.md Docs/prd.md
git commit -m "test: cover CesiumAI MVP acceptance flows"
```

---

## Final Review Checklist

- [ ] `Docs/prd.md` requirements map to at least one task above.
- [ ] `clear`, facility upsert/delete, and SSO/J2 upsert are covered by unit,
  HTTP integration, and browser tests.
- [ ] Every scene-changing packet originates in C# tools.
- [ ] Frontend document state and `CzmlDataSource` stay synchronized after
  successful operations.
- [ ] Session-scoped agent runtimes cannot leak operations between requests.
- [ ] No automated test calls a live model or Astrox endpoint.
- [ ] Secrets and `backend/skills` are ignored by Git.
- [ ] `dotnet test`, `npm test -- --run`, `npm run build`, and `npm run e2e`
  all exit 0.
