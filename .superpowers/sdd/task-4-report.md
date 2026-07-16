# Task 4 Report: Typed Astrox SSO/J2 Pipeline

## Status

- Implemented a typed Astrox SSO -> J2 backend pipeline for .NET 10.
- Kept all automated coverage on in-memory `HttpMessageHandler`; no live Astrox calls are made in tests.
- Deferred any Agent or SceneTools integration as requested.

## Delivered Files

### Production

- `backend/CesiumAI.Api/Configuration/AstroxOptions.cs`
- `backend/CesiumAI.Api/Astrox/AstroxContracts.cs`
- `backend/CesiumAI.Api/Astrox/AstroxClient.cs`
- `backend/CesiumAI.Api/Astrox/OrbitScenarioService.cs`

### Tests

- `backend/CesiumAI.Api.Tests/TestSupport/StubHttpMessageHandler.cs`
- `backend/CesiumAI.Api.Tests/Astrox/AstroxClientTests.cs`
- `backend/CesiumAI.Api.Tests/Astrox/OrbitScenarioServiceTests.cs`

## TDD Notes

### RED -> GREEN: AstroxClient

1. Added failing tests for:
   - PascalCase JSON payloads to `POST /OrbitWizard/SSO`
   - PascalCase JSON payloads to `POST /Propagator/J2`
   - HTTP failure wrapping with endpoint and Astrox `Message`
   - `IsSuccess == false` body failures
   - `200 OK` with empty/whitespace body for both SSO and J2
2. Confirmed the initial red state with:

```bash
dotnet test CesiumAI.slnx --filter "AstroxClientTests"
```

3. Implemented typed options/contracts/client with millisecond UTC formatting and case-sensitive outbound JSON.
4. Re-ran the same focused test target until green.

### RED -> GREEN: OrbitScenarioService

1. Added failing tests for:
   - SSO before J2 call order
   - classical element order in J2 payload
   - complete satellite CZML packet shape and values
   - `AstroxException` propagation on either Astrox failure, with no returned packet
   - scenario input validation
2. Confirmed the initial red state with:

```bash
dotnet test CesiumAI.slnx --filter "OrbitScenarioServiceTests"
```

3. Implemented orchestration/validation/CZML assembly.
4. Re-ran the focused service tests until green.

### Review Fixes: Important Follow-Up

1. Added failing tests to lock the reviewed behavior:
   - `IOrbitScenarioService.CreateSsoJ2PacketAsync(...): Task<JsonElement>` at compile time
   - SSO/J2 failures propagate `AstroxException` instead of returning `null`
   - `200 OK` with empty or whitespace body throws `AstroxException` with endpoint + `empty response body`
2. Confirmed RED with:

```bash
dotnet test CesiumAI.slnx --filter "FullyQualifiedName~Astrox"
```

The red state initially failed on the service signature mismatch (`Task<JsonElement?>` vs `Task<JsonElement>`), which was the intended review issue.

3. Implemented the minimal production changes:
   - restored `IOrbitScenarioService` to `Task<JsonElement>`
   - removed `OrbitScenarioService` catch/swallow behavior for `AstroxException`
   - changed `AstroxClient` success-path body handling to `ReadAsStringAsync` -> `string.IsNullOrWhiteSpace` guard -> `JsonSerializer.Deserialize`
4. Re-ran the focused Astrox tests to GREEN.

## Requirement Coverage

- PascalCase outbound JSON: covered in `AstroxClientTests`
- Locked endpoint order `POST /OrbitWizard/SSO` then `POST /Propagator/J2`: covered in `OrbitScenarioServiceTests`
- Orbital element order: covered in `OrbitScenarioServiceTests`
- Error path returns no packet to callers because `AstroxException` now propagates from both SSO and J2 failures
- Complete satellite CZML packet: covered with `id`, `name`, `availability`, `position`, `point`, `path`, and `properties.orbitHint.string`
- No live Astrox access in tests: enforced via `StubHttpMessageHandler`
- Empty/whitespace `200 OK` body handling: covered for both SSO and J2

## Final Verification

Latest verification commands and results:

```bash
dotnet test CesiumAI.slnx --filter "FullyQualifiedName~Astrox"
# Passed: 19, Failed: 0, Skipped: 0

dotnet test CesiumAI.slnx
# Passed: 24, Failed: 0, Skipped: 0
```

## Remaining Follow-Ups / Attention Points

- The new Astrox client/service types are implemented and tested but not yet wired into ASP.NET DI or any endpoint/agent flow; that was intentionally left out of Task 4 scope.
- `IOrbitScenarioService.CreateSsoJ2PacketAsync` now returns `JsonElement`; Astrox failures are surfaced as `AstroxException`, so callers never receive a packet on failure.

## Commit / Push

- Original Task 4 implementation was committed and pushed.
- This report section is updated in-place for the follow-up review fix pass; see git history for the latest follow-up commit that restores exception propagation and empty-body handling.
