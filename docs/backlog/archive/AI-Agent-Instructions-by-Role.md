# Backlog: AI Agent Instructions by Role (Config-Seeded, Admin-Overridable)

**Status:** **Implemented** — all 6 items delivered in Sprint 77 (2026-08-16). See `docs/sprints/Sprint-77 - AI Agent Instructions by Role.md`.
**Created:** 2026-08-11
**Source:** Design review with the project owner of where AI agent instructions live today and how operators tune them. Agent personas/instructions are currently split across `ChatAgentOptions` (`appsettings.json` `ChatAgent` section), the orchestration script file `src/Repository.API/Prompts/copilot-rags-orchestration.md`, and hard-coded system prompts in `SemanticKernelChatService.BuildSystemPrompt()` and the GraphIntelligence services (`EntityExtractionService`, `GraphReasoningService`). The Sprint 61 settings foundation (`app_settings` + `ISettingsService` singleton + `GET/PUT /api/settings`, admin) provides the natural home for runtime overrides.

## Problem

AI agent instructions (the system prompts/personas that shape how each agent role behaves) are **code- and config-only** today:

- Changing a prompt requires editing `appsettings.json` / a `.md` file and redeploying the API container.
- There is no per-environment or per-deployment way for an operator to tune a prompt at runtime, and no audit trail of what was changed vs. the shipped baseline.
- Different LLM tasks (Copilot assistant, entity extractor, summarizer, graph-query coder) each carry their own instructions, but there is no single "instructions" surface to see or edit them together.

## Decisions made (2026-08-11)

1. **Precedence = config seed, settings override.** `appsettings.json` (bound `AgentInstructions` section) is the **baseline**. The admin Settings panel writes per-role overrides into `app_settings`. A role's **effective** instructions are: the `app_settings` row when one exists, otherwise the config value. Row-existence *is* the "modified" marker — no dirty flag or timestamp is tracked.
2. **"Reset to config default" is a first-class action.** Clearing the override row (explicit reset button + `DELETE`) returns the role to the config baseline. Without this, an admin could never undo an override.
3. **"Role" means agent role, not user role.** The keyed roles are the LLM tasks (see Work Items). This is not RBAC — user roles (Administrator/…) are explicitly out of scope.
4. **One `app_settings` row per role** (`agent.instructions.<role>`), matching the existing key convention (`copilot.requireApproval`, …), with typed `GetStringAsync`/`SetStringAsync` accessors added to `ISettingsService`. No JSON-blob-per-whole-dictionary rows.
5. **Admin-only writes, any-authenticated reads** where needed — same governance pattern as the existing settings: the API enforces the role, the UI hides the card for non-admins.
6. **Config section keys mirror DB keys** so precedence resolution is a pure lookup, not a mapping layer.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Settings foundation extension** — add `GetStringAsync`/`SetStringAsync` (and a clear/delete) to `ISettingsService`/`SettingsService`; the underlying `app_settings` table already supports arbitrary string values. | The existing service is bool-typed; agent instructions are free text. | ~0.25–0.5 day | Proposed |
| 2 | **Config section + role registry** — new `AgentInstructionsOptions` bound section (default role list + prompts, mirroring `ChatAgentOptions` style) and a canonical role key registry (e.g. `copilot.assistant`, `copilot.orchestrator`, `graphrag.extractor`, `graphrag.summarizer`, `graphrag.query`). Unknown roles are rejected at the API boundary. | Gives the baseline prompts a single, code-reviewed home and a validated role enumeration. | ~0.5 day | Proposed |
| 3 | **Precedence resolver** — `AgentInstructionResolver` (Application) implementing `ResolveAsync(role)`: `app_settings` row exists → DB value; else → config value. Injected into every prompt-building consumer so they resolve through it instead of reading options/file only (`SemanticKernelChatService.BuildSystemPrompt`, the orchestration-script loader, `EntityExtractionService`, `GraphReasoningService`). | This is the core behavior: config stays the baseline, the panel overrides per role. | ~1–1.5 days | Proposed |
| 4 | **API surface** — extend `SettingsController`: `GET /api/settings/agent-instructions` (list roles + effective value + source `config`/`override`), `PUT /api/settings/agent-instructions/{role}` (write override, Administrator), `DELETE /api/settings/agent-instructions/{role}` (reset to config, Administrator). Validation: max length, non-empty override, known role. | Operators need a programmatic surface and an explicit reset; the source field makes precedence visible. | ~0.5–1 day | Proposed |
| 5 | **Admin Settings panel card** — "AI Agent Instructions" card on `/settings` (behind the existing admin gate): role list with edit controls, a per-role override indicator (config vs. customized), and a "Reset to config default" button that calls `DELETE`. | Gives admins the first-class surface the item is about; the panel is where overrides happen. | ~0.5–1 day | Proposed |
| 6 | **Tests** — precedence unit tests (row exists → DB; absent → config; reset → config), resolver tests across all prompt consumers, controller auth/validation tests, Web binding tests. | The precedence rule is the whole point; it must be locked down. | ~0.5 day | Proposed |

## Suggested Sequencing

- **Items 1 + 2 first** — the foundation (string accessors) and the config/registry shape are prerequisites for everything else.
- **Item 3 next** — the resolver is the core deliverable; it can land with consumers updated in the same pass.
- **Items 4 + 5 together** — the API and the panel card are two halves of the same feature.
- **Item 6 alongside each** — precedence tests per item, not a trailing batch.

**Total (agent):** ~3–4 working days including build/test verification.

## Out of Scope

- User-role RBAC changes (Administrator/… role permissions).
- Per-user agent instructions (this is global/app-level; per-user stays `user_settings` territory if ever wanted).
- Prompt-versioning history / rollback beyond "reset to config baseline".
- Secrets or credentials in agent instructions — admin-only plain text, not treated as sensitive.
