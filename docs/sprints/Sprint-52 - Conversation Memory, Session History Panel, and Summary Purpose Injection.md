# Sprint 52 - Conversation Memory, Session History Panel, and Summary Purpose Injection

**Status:** Active (implementation complete; pending end-to-end Copilot verification)

## Objective

Fix two follow-up gaps reported after Sprint 51:

1. **Conversation memory / follow-up grounding** - a follow-up question such as "What is the nature of each one of these RFPs?" must inherit the sources (and prior turns) from the same conversation, instead of re-resolving (and collapsing to one document).
2. **Summary purpose injection** - a document summary must include the document's stated purpose/theme/main topic (its "Project Summary" section), which vector top-k retrieval often misses.
3. **Session history panel** - a right-side "Chats" panel (alongside the existing Activity panel) listing past conversations, with open/delete, backed by localStorage.

## Background

- The engine executes each job with a fresh `new ChatSession()` (`RunJobAsync` -> `_copilotService.ChatAsync`), so no conversation history or prior source context reaches synthesis or retrieval. The web UI already persists one active `ChatSession` in localStorage (`CopilotStateService`), but only one session, and it is not sent with the plan.
- Per-source summary retrieval returns only ~2-5 top-k chunks per document; the chunk containing the document's purpose/"Project Summary" section usually does not rank there, so the model cannot state it.
- The right rail has an `ActivityPanel`; a sibling "Chats" panel (or tabbed rail) is the natural place for session history.

## Deliverables

1. **Engine conversation memory**
   - `ChatExecutionPlan` gains optional `SessionId` and `HistoryMessages`.
   - `PlanPayload` (POST /api/copilot/plan) gains optional `sessionId` and `historyMessages`; controller threads them into plan creation.
   - Engine keeps a per-session memory of the last resolved sources (`ConcurrentDictionary<Guid, SessionMemory>`).
   - Follow-up fallback: when source resolution finds no new match and the prompt uses reference language ("these/those/this/that/them/they/it/each" or collection intent), the previous turn's sources are used as the scope.
   - Synthesis uses a `ChatSession` seeded with `HistoryMessages` (and the session id) instead of an empty one.

2. **Summary purpose injection**
   - Per-source summary retrieval adds a purpose-oriented query variant ("project summary purpose objective scope of work overview") and a slightly larger per-source top-k for collection prompts.
   - `RetrievalAugmentedPromptBuilder` instructs: every document summary must state the document's purpose/theme/main topic from its opening/summary section when present in context.
   - Orchestration playbook updated with the same directive.

3. **Session history panel (web)**
   - `CopilotStateService`: persist a bounded list of recent sessions (localStorage `aletheia.copilot.recentSessions.v1`, max ~10); add/remove/clear + open-event.
   - `ChatsPanel.razor`: right-side collapsible panel listing sessions (title, date, message count) with Open/Delete; wired into `MainLayout` next to `ActivityPanel`.
   - Copilot page: save the current session to history on new-chat, raise/consume the open-session event to restore a conversation, and send `sessionId` + history with the plan request.

## Requirements (Detailed)

### Engine memory
- `ChatExecutionPlan`: `Guid? SessionId { get; init; }`, `IReadOnlyList<ChatMessage>? HistoryMessages { get; init; }`.
- `IChatPlanApprovalService.CreatePlanAsync(prompt, ...)` gains optional `sessionId`/`history` parameters (default null) and stores them on the plan.
- `ChatExecutionEngine`:
  - `private sealed record SessionMemory(IReadOnlyList<KnowledgeSource> Sources, DateTimeOffset UpdatedAt);`
  - After retrieval completes, record distinct sources (from retrieval results / resolved scope) under `Plan.SessionId` when present.
  - `ResolvePromptSourceScopeAsync(query, ct, priorSources)`: if normal resolution returns null and `priorSources` is non-empty and the prompt has reference/collection intent, return a multi-source scope of the prior sources.
  - Synthesis: build `ChatSession` with `Id = Plan.SessionId ?? Guid.NewGuid()` and `Messages = Plan.HistoryMessages ?? empty`, then call `ChatAsync(session, item.Prompt, ...)`.

### Purpose injection
- `RetrieveScopedCollectionResultsAsync`: when `HasCollectionIntent(query)`, extend query variants with "project summary purpose objective scope of work overview" and clamp per-source top-k to 3..6.
- `RetrievalAugmentedPromptBuilder`: add the purpose-statement instruction.

### Web history panel
- `CopilotStateService`: `RecentSessions` (max 10), `SaveCurrentSessionAsync`, `RemoveSessionAsync`, `ClearSessionsAsync`, `event Action<ChatSession>? SessionOpened`.
- `ChatsPanel.razor` + CSS mirroring `ActivityPanel`; `MainLayout` renders it beside the activity panel.
- Copilot `Index.razor`: on new chat, persist the old session; subscribe to `SessionOpened` to restore; send `sessionId` + `historyMessages` with the plan.

## Acceptance Criteria

- A follow-up "What is the nature of each one of these RFPs?" after a 2-RFP question retrieves the same 2 sources (engine test).
- The session passed to synthesis contains the prior history messages (engine test).
- Summary prompts include the purpose-oriented retrieval variant (unit-verifiable).
- `CopilotStateService` persists/removes/clears recent sessions (web unit tests).
- RAGS.UnitTests, Aletheia.Foundation.UnitTests, Repository.UnitTests remain green; Aletheia.Web.UnitTests green where runnable.


## Execution Status (2026-08-03)

Implemented and verified:

- **Conversation memory**: `ChatPlanRecord` now carries optional `SessionId` + `HistoryMessages`; `PlanPayload` and `PlanChatAsync` thread them through; the engine records per-session resolved sources and reuses them for follow-up prompts with reference language ("these/those/second/next/..."); synthesis now receives a `ChatSession` seeded with prior history. Fixed plan-record rebuild paths in `InMemoryChatPlanRepository.UpdateStatusAsync` and `ChatPlanApprovalService.CancelAsync` that dropped the new fields.
- **Summary purpose injection**: per-source collection retrieval adds a purpose-oriented query variant and a 3..6 per-source top-k for collection prompts; the prompt builder and orchestration playbook require summaries to state the document's purpose/theme/Project Summary.
- **Chats panel**: `CopilotStateService` persists up to 10 recent sessions in localStorage (`aletheia.copilot.recentSessions.v1`), with open/delete/clear; new `ChatsPanel.razor` (right rail, next to Activity); Copilot page saves sessions on new-chat, restores on open, and sends `sessionId` + history with each plan.

Tests:

- RAGS.UnitTests: 202/202 (includes `Engine_uses_prior_session_sources_for_followup_prompt`, `Engine_passes_session_history_to_synthesis`).
- Aletheia.Foundation.UnitTests: 55/55; Repository.UnitTests: green.
- Aletheia.Web C# compiles (`-t:CoreCompile`); Aletheia.Web.UnitTests (incl. 3 new CopilotStateService history tests) must run on a normal machine - the Blazor WASM `ComputeWasmBuildAssets` task cannot spawn its out-of-proc task host in the sandbox.
