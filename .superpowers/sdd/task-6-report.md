# Task 6 Report

## Status

Implemented Microsoft Agent Framework integration, per-session agent/session reuse, same-session turn serialization, request-scoped scene-operation collection, scene prompt construction, constrained Astrox HTTP tools, and the `IChatService` boundary.

## Behavior delivered

- `ScenePromptBuilder` emits only `[SCENE_SUMMARY]`, `[RELEVANT_CZML_PACKETS]`, and `[USER]` in the required order, using compact camel-case JSON and `[]` for absent packets.
- `ChatService` normalizes blank session IDs to GUIDs, preserves supplied IDs, creates and drains exactly one collector after a successful turn, and never derives scene operations from agent text.
- `AgentRuntimeStore` stores `ConcurrentDictionary<string, Lazy<Task<AgentRuntime>>>`.
  - One runtime contains the production `AIAgent`, `AgentSession`, semaphore, and scene-tool sink.
  - Same-session turns are serialized.
  - Different sessions can execute concurrently.
  - `TurnSceneOpSink.Current` is bound only while the turn owns the semaphore and is cleared in `finally`.
  - Scene writes outside an active turn throw.
- `AgentFactory` creates a Chat Completions agent with all six required tools and one framework session per runtime.
- `AgentInstructions.Text` is the single source of the five required policies.
- Skills paths are resolved against `IHostEnvironment.ContentRootPath`; missing directories throw a clear `DirectoryNotFoundException` when `AgentFactory` is activated.
- `AstroxRawTools` accepts only root-relative paths on the configured Astrox origin, rejects absolute/scheme-relative/traversal paths, returns HTTP status plus body, and has no scene collector dependency.

## TDD evidence

Observed expected RED compilation failures before implementation:

- `ScenePromptBuilderTests`: missing `ScenePromptBuilder`
- `ChatServiceTests`: missing `IAgentTurnRunner`
- `AgentRuntimeStoreTests`: missing runtime/store/sink types
- `AstroxRawToolsTests`: missing `AstroxRawTools`
- `AgentFactoryTests`: missing `AgentFactory`

Each group was then implemented and run GREEN before proceeding.

## Verification

```text
dotnet test CesiumAI.slnx --filter "AgentRuntimeStoreTests|AstroxRawToolsTests|ChatServiceTests|ScenePromptBuilderTests|AgentFactoryTests"
Passed: 26, Failed: 0, Skipped: 0

dotnet test CesiumAI.slnx
Passed: 62, Failed: 0, Skipped: 0
```

All automated tests use fakes, temporary local skill directories, or in-memory HTTP handlers. No live LLM or Astrox request is made.

## Agent Framework 1.13 API note

The installed `Microsoft.Agents.AI.OpenAI` 1.13.0 public API matched the plan's example: `OpenAIClient`, `GetChatClient(...).AsAIAgent(ChatClientAgentOptions)`, `ChatOptions.Tools`, `AIContextProviders`, `CreateSessionAsync`, and `RunAsync(prompt, session, cancellationToken: ...)` compiled without behavioral adaptation.

## Attention

- `backend/skills` is not present in the repository. This task intentionally does not vendor the external Astrox skills; activating `AgentFactory` with the default `../skills` path will fail clearly until that directory is installed.
- ASP.NET controller, configuration binding/validation, dependency registration, and endpoint wiring remain outside this task's file scope and are planned for Task 7.
