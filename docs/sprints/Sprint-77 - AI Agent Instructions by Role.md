# Sprint 77 - AI Agent Instructions by Role

**Status:** Active (2026-08-16)

Full authority: this file. Sprint 76 (Graph Drag-Group and Scale Slider) is **complete, committed, and pushed** on `origin/master` (`8eb3d6a`).

Promotes `docs/backlog/AI-Agent-Instructions-by-Role.md` — the design-review outcome (2026-08-11) of where AI agent instructions live today and how operators tune them. Agent personas/instructions are currently split across `ChatAgentOptions` (`appsettings.json` `ChatAgent` section), the orchestration script file `src/Repository.API/Prompts/copilot-rags-orchestration.md`, and hard-coded system prompts in `SemanticKernelChatService.BuildSystemPrompt()` and the GraphIntelligence services (`EntityExtractionService`, `GraphReasoningService`, `GraphSummaryService`). The Sprint 61 settings foundation (`app_settings` + `ISettingsService` singleton + `GET/PUT /api/settings`, admin) provides the natural home for runtime overrides.

## Objective

Give operators a single, admin-managed surface to view and tune the AI agent system prompts at runtime — config stays the shipped baseline, `app_settings` overrides win, and "reset to config default" is a first-class action. Six work items:

1. **Settings foundation extension** — `GetStringAsync`/`SetStringAsync`/`ClearAppSettingAsync` on `ISettingsService`/`SettingsService` (+ `DeleteAppSettingAsync` on `ISettingsRepository`).
2. **Config section + role registry** — `AgentInstructionsOptions` bound section + canonical role key registry (`AgentInstructionRoles`).
3. **Precedence resolver** — `AgentInstructionResolver` (Application) implementing `ResolveAsync(role)`: `app_settings` row exists → DB value; else → config value; wired into every prompt-building consumer.
4. **API surface** — `GET /api/settings/agent-instructions` (list roles + effective value + source), `PUT /api/settings/agent-instructions/{role}` (write override, Administrator), `DELETE /api/settings/agent-instructions/{role}` (reset to config, Administrator).
5. **Admin Settings panel card** — "AI Agent Instructions" card on `/settings` (admin-gated): per-role edit controls, Customized/Config-default badge, Reset button.
6. **Tests** — precedence + resolver + controller + Web binding tests.

## Decisions (from the backlog item, settled 2026-08-11)

1. **Precedence = config seed, settings override.** `appsettings.json` (bound `AgentInstructions` section) is the **baseline**. The admin Settings panel writes per-role overrides into `app_settings`. A role's **effective** instructions are: the `app_settings` row when one exists, otherwise the config value. Row-existence *is* the "modified" marker — no dirty flag or timestamp is tracked.
2. **"Reset to config default" is a first-class action.** Clearing the override row (explicit reset button + `DELETE`) returns the role to the config baseline.
3. **"Role" means agent role, not user role.** The keyed roles are the LLM tasks. Not RBAC — user roles (Administrator/…) are out of scope.
4. **One `app_settings` row per role** (`agent.instructions.<role>`), matching the existing key convention, with typed string accessors added to `ISettingsService`. No JSON-blob-per-whole-dictionary rows.
5. **Admin-only writes, any-authenticated reads** where needed — same governance pattern as the existing settings: the API enforces the role, the UI hides the card for non-admins.
6. **Config section keys mirror DB keys** so precedence resolution is a pure lookup, not a mapping layer.

## Deliverables

### 1. Settings foundation extension
- `ISettingsService` gains `GetStringAsync(string key, string? userId = null, ...)` → `Result<string?>` (null when missing), `SetStringAsync(string key, string value, string? userId = null, ...)`, `ClearAppSettingAsync(string key, ...)`.
- `ISettingsRepository` gains `DeleteAppSettingAsync(string key, ...)`; `PostgreSqlSettingsRepository` implements it (`DELETE FROM app_settings WHERE key = @Key`).
- `SettingsService` implements all three: `GetStringAsync` reads the app cache (or per-user cache), `SetStringAsync` delegates to `SetAppSettingAsync`/`SetUserSettingAsync`, `ClearAppSettingAsync` deletes the row and evicts the cache.

### 2. Config section + role registry
- `AgentInstructionsOptions` (`SectionName = "AgentInstructions"`, `Dictionary<string, string> Roles`) bound in `AIServiceCollectionExtensions`.
- `AgentInstructionRoles` — canonical registry: `copilot.assistant`, `copilot.orchestrator`, `graphrag.extractor`, `graphrag.summarizer`, `graphrag.query`; `IsKnown(role)`, `SettingKey(role)` → `agent.instructions.<role>`.
- `appsettings.json` `AgentInstructions` section seeds the three GraphRAG prompts (`graphrag.extractor`, `graphrag.summarizer`, `graphrag.query`). `copilot.assistant`/`copilot.orchestrator` are intentionally **not** in config — the resolver composes the persona from `ChatAgentOptions` / loads the orchestration file as their role-specific baselines (avoids duplicating the persona and drifting).

### 3. Precedence resolver + consumers
- `AgentInstructionResolver` (`RAGS.Application/AgentInstructions`) implementing `IAgentInstructionResolver.ResolveAsync(role)` → `AgentInstructionResolution(role, value, source)` where `source` is `"override"` (DB row) or `"config"` (baseline). Unknown roles fail. `ResolveConfigBaseline`: `AgentInstructions` section value → role-specific baseline (`ComposeAssistantPersona` from `ChatAgentOptions` for `copilot.assistant`; `LoadOrchestrationScript` from `ChatAgentOptions.OrchestrationScriptPath` for `copilot.orchestrator`; empty otherwise).
- Consumers resolve through it (optional ctor param, hard-coded fallback preserved):
  - `SemanticKernelChatService` — `BuildSystemPromptAsync` resolves `copilot.assistant` (fallback `ComposeAssistantPersona`).
  - `FileChatAgentInstructionProvider` — `GetInstructionsAsync` resolves `copilot.orchestrator` first, falls back to file loading. `IChatAgentInstructionProvider` became async.
  - `EntityExtractionService` — resolves `graphrag.extractor`.
  - `GraphReasoningService` — resolves `graphrag.query`.
  - `GraphSummaryService` — resolves `graphrag.summarizer`.
- Registered in `AIServiceCollectionExtensions` (`AddSingleton<IAgentInstructionResolver, AgentInstructionResolver>()`).

### 4. API surface (`SettingsController`)
- `GET agent-instructions` (Administrator) — iterates `AgentInstructionRoles.All`, resolves each, returns `[{ role, value, source }]`.
- `PUT agent-instructions/{role}` (Administrator) — validates known role, non-empty value, ≤ 20,000 chars; `SetAppSettingAsync(SettingKey(role), value, CurrentUserId)`.
- `DELETE agent-instructions/{role}` (Administrator) — validates known role; `ClearAppSettingAsync(SettingKey(role))` → NoContent.

### 5. Admin Settings panel card (`Pages/Settings/Index.razor`)
- "AI Agent Instructions (Administrator)" card inside `AuthorizeView Roles="Administrator"` after the existing row.
- Per role: role name + source badge (`bg-warning text-dark` "Customized" / `bg-light text-dark border` "Config default"), textarea bound to `item.Value`, **Save** button, **Reset to config default** button (disabled when `item.Source != "override"`).
- `RepositoryApiClient` gains `GetAgentInstructionsAsync` / `UpdateAgentInstructionAsync` / `ResetAgentInstructionAsync`.

### 6. Tests
- **RAGS** — `AgentInstructionResolverTests` (9): DB override wins over config; config when no row; config after row cleared; whitespace row ignored; unknown role fails; assistant persona composed from `ChatAgentOptions`; orchestration script loaded from file; empty baseline for roles without one.
- **Repository** — `AgentInstructionsControllerTests` (8): GET returns all roles; GET 500 when resolver missing; PUT saves override (verifies key); PUT rejects unknown role / empty value / over-length; DELETE clears row (verifies key); DELETE rejects unknown role. `SettingsServiceTests` (+5): string accessors round-trip app/user scope, null when missing, clear removes row, clear idempotent.
- **Web** — `SettingsAgentInstructionsBindingTests` (5): card rendered, badge logic, Save/Reset buttons, admin load, client endpoints.

## Acceptance Criteria

- A role's effective instructions are the `app_settings` row when one exists, otherwise the config baseline; clearing the row returns the role to baseline.
- The admin Settings panel shows every role with its effective value and source, and can save an override or reset to config default.
- All five prompt consumers resolve through the resolver; the hard-coded prompts remain the fallback when no resolver/config value exists.
- Repository + Web + RAGS + Foundation unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- User-role RBAC changes (Administrator/… role permissions).
- Per-user agent instructions (global/app-level only; per-user stays `user_settings` territory).
- Prompt-versioning history / rollback beyond "reset to config baseline".
- Secrets or credentials in agent instructions — admin-only plain text, not treated as sensitive.

---

## Implementation Status

**Implemented (2026-08-16).** All 6 items complete; tests green.

### Item 1 — Settings foundation extension
- `ISettingsService` gains `GetStringAsync` (`Result<string?>`, null when missing), `SetStringAsync`, `ClearAppSettingAsync`; `ISettingsRepository` gains `DeleteAppSettingAsync`; `PostgreSqlSettingsRepository` implements it (`DELETE FROM app_settings WHERE key = @Key`); `SettingsService` implements all three (cache read / delegate to set / delete + evict).

### Item 2 — Config section + role registry
- `AgentInstructionsOptions` (`SectionName = "AgentInstructions"`, `Roles` dictionary) + `AgentInstructionRoles` (5 roles, `IsKnown`, `SettingKey`). `appsettings.json` seeds the three GraphRAG prompts; `copilot.assistant`/`copilot.orchestrator` stay out of config (composed/loaded baselines).

### Item 3 — Precedence resolver + consumers
- `AgentInstructionResolver` (`RAGS.Application/AgentInstructions`) — DB override → config value → role-specific baseline; `AgentInstructionResolution(role, value, source)`. Wired into `SemanticKernelChatService` (`copilot.assistant`), `FileChatAgentInstructionProvider` (`copilot.orchestrator`, now async), `EntityExtractionService` (`graphrag.extractor`), `GraphReasoningService` (`graphrag.query`), `GraphSummaryService` (`graphrag.summarizer`). Registered in `AIServiceCollectionExtensions`.

### Item 4 — API surface
- `SettingsController` — `GET agent-instructions` (list + source), `PUT agent-instructions/{role}` (validate known role / non-empty / ≤ 20k, write override), `DELETE agent-instructions/{role}` (reset → NoContent). All Administrator.

### Item 5 — Admin Settings panel card
- `Pages/Settings/Index.razor` — "AI Agent Instructions (Administrator)" card (admin-gated): per-role textarea + Customized/Config-default badge + Save + Reset (disabled on config default). `RepositoryApiClient` gains the three client methods.

### Item 6 — Tests
- **RAGS 369 (+9)**: `AgentInstructionResolverTests` — precedence (DB wins / config / after-clear / whitespace-ignored), unknown role, persona composition, orchestration file load, empty baseline.
- **Repository 170 (+13)**: `AgentInstructionsControllerTests` (8) + `SettingsServiceTests` string-accessor tests (5).
- **Web 144 (+5)**: `SettingsAgentInstructionsBindingTests` — card, badge, buttons, admin load, client endpoints.
- Foundation 55 unchanged; `dotnet build Aletheia.slnx` succeeds (0 errors). Docs updated; backlog item archived.

**Residual manual (user-side):** `docker compose up -d --build`, then hard-refresh `/settings` as an Administrator — the **AI Agent Instructions** card lists all five roles with their effective prompts; edit one and **Save** (badge flips to **Customized**), then **Reset to config default** (badge returns to **Config default**). Re-ask a Copilot question or run a GraphRAG search to see the override take effect. No schema migration — the `app_settings` table already exists (Sprint 61).
