# Sprint 61 - Chat Approval Prompt and Admin Settings

**Status:** Active

Full authority: this file. Sprint 60 (GraphRAG and LazyGraphRAG Quick Wins) is **committed and pushed** (`c6c3e48` on `origin/master`); its optional Docker smoke test remains as a parallel verification task.

Promotes all 5 items from `docs/backlog/Chat-Approval-Prompt-and-Admin-Settings.md` (created 2026-08-08).

## Objective

Fix the Copilot chat approval flow so the plan-approval prompt is never hidden, then give users and admins first-class control over when approval is required:

1. **Modal approval prompt (visibility fix)** — render the plan preview in a centered modal overlay above the Activity/Chats panels so a submitted prompt always surfaces its approval request; auto-expand a collapsed Execution column on submit.
2. **Server-side settings foundation** — `app_settings` / `user_settings` tables + migration + `init.sql`; singleton `SettingsService`; `GET/PUT /api/settings` (admin) and `GET/PUT /api/settings/me` (authenticated).
3. **Chat approval preference** — `copilot.requireApproval`, per-user, **default true**; "Don't ask again" checkbox on the modal writes the preference; when off, plans auto-approve and execute immediately.
4. **Admin override for approval** — admin-managed global/role setting that forces approval even for opted-out users.
5. **Admin Settings page** — `/settings` gated to Administrator, listing global settings with edit controls; users see their own editable preferences.

## Background

- The plan approval prompt (`PlanPreview` with the **Run** button) renders in the right-hand Execution column of `/copilot`. `ActivityPanel` (`z-index: 20`) and `ChatsPanel` (`z-index: 21`) are `position: fixed; right: 0` overlays that cover exactly that region when open — a submitted prompt renders its approval prompt **behind** the open panel, so the user sees "Copilot is thinking…" then nothing and assumes the chat is broken.
- The Execution column can also be collapsed to a "Show execution" button, hiding the approval prompt with no panels open.
- There is no settings infrastructure today — only localStorage state services; "manage settings as an admin task" requires a server-side home.

**Decisions made (2026-08-08):** (1) fix via a **modal** approval prompt, not auto-closing the side panels; (2) the opt-out is per-user **with an admin override** (an admin-managed global/role setting that forces approval even for users who opted out); (3) the approval prompt stays **active by default** (users must explicitly opt out).

## Deliverables

### 1. Modal approval prompt (visibility fix)
- Render `PlanPreview` inside a centered modal overlay with a z-index above the Activity/Chats panels (currently `20`/`21`), shown whenever a plan is awaiting approval/run.
- Auto-expand a collapsed Execution column on submit so the prompt and later progress are always visible.
- Keep the existing plan preview in the Execution column as the in-context summary.

### 2. Server-side settings foundation
- New `app_settings` (global, admin-managed) and `user_settings` (per-user) tables + idempotent migration + `init.sql`.
- Singleton `SettingsService` with typed accessors and caching.
- API `GET/PUT /api/settings` (admin) and `GET/PUT /api/settings/me` (authenticated).

### 3. Chat approval preference (first setting)
- `copilot.requireApproval`, per-user, **default true**.
- A "Don't ask again" checkbox on the approval modal (item 1) writes the preference.
- When off, plans auto-approve and execute immediately after planning (progress + cancel still visible in the Execution column).

### 4. Admin override for approval
- An admin-managed global/role setting that forces the approval prompt even for users who opted out, so admins can gate expensive corpus-wide operations for designated roles regardless of user preference.

### 5. Admin Settings page
- `/settings` page gated to the Administrator role (same pattern as Governance), listing global settings with edit controls, plus an admin-only NavMenu entry; users see their own editable preferences.

## Acceptance Criteria

- With the Activity or Chats panel open, submitting a chat prompt shows the approval prompt in a modal above the panels; the user can Run/Revise/Cancel.
- A collapsed Execution column auto-expands on submit; progress remains visible after approval.
- Settings foundation: `app_settings`/`user_settings` tables exist (migration + `init.sql` in sync); `SettingsService` caches; admin and per-user endpoints work.
- `copilot.requireApproval` defaults true; the modal's "Don't ask again" persists the preference; opting out auto-approves and executes.
- Admin override forces approval for opted-out users.
- `/settings` is Administrator-gated; users see their own preferences.
- RAGS / Repository / Foundation / Web unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Persisting the LazyGraphRAG corpus index to PostgreSQL (GraphRAG backlog item 2); batch GraphRAG ingest (GraphRAG backlog item 3); theme-aware graph retrieval (Canonical backlog item 5).

---

## Progress

### Sprint 61 item 1 — modal approval prompt (2026-08-10)

**Implemented.** The plan-approval prompt is no longer hidden behind the Activity/Chats panels or a collapsed Execution column:

- `Index.razor` renders `PlanPreview` inside a centered modal overlay (`.copilot-approval-backdrop` / `.copilot-approval-modal`, `z-index: 1050` — above the panels' `20`/`21`) whenever a plan is awaiting approval/run (`IsPlanPreviewVisible && _pendingPlan?.Status == ChatPlanStatus.Proposed`). The modal reuses the existing `PlanPreview` component (Run/Revise/Cancel), so there is no duplicated markup; the in-context plan preview stays in the Execution column.
- `SendChat()` now auto-expands a collapsed Execution column on submit, so the approval prompt and later progress are always visible.
- CSS added to `Index.razor.css` (fixed backdrop, centered card, `max-height` + scroll).

**Verification:** `dotnet build src/Aletheia.Web/Aletheia.Web.csproj` 0 warnings/0 errors; Aletheia.Web.UnitTests 39/39 green (binding tests still pass). Full solution build + unit suites green.

**Next:** items 2 + 3 + 4 (settings foundation + approval preference + admin override) in one pass, then item 5 (admin Settings page).
