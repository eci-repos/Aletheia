# Backlog: Chat Approval Prompt Visibility and Admin-Managed Settings

**Status:** Proposed (2026-08-08); items are **not** authorized work — an item becomes authorized only when the current sprint file promotes it.
**Created:** 2026-08-08
**Source:** UX review of the Copilot chat approval flow (`src/Aletheia.Web/Pages/Copilot/Index.razor`, `src/Aletheia.Web/Layout/ActivityPanel.razor`/`ChatsPanel.razor` + CSS, `ChatPlanningService.RequiresApproval`, `ChatPlanningOptions`) — reported issue: with the Activity/Chats side panel open, submitting a chat prompt hides the plan-approval prompt behind the fixed `z-index: 20` overlay, so the user sees nothing happen.

## Problem

The plan approval prompt (`PlanPreview` with the **Run** button, rendered in the right-hand Execution column of `/copilot`) is easily hidden:

- `ActivityPanel` and `ChatsPanel` are `position: fixed; right: 0; z-index: 20` overlays that cover exactly the region where the Execution column sits when open. A submitted prompt renders its approval prompt **behind** the open panel → user sees "Copilot is thinking…" then nothing → assumes the chat is broken.
- The Execution column can also be collapsed to a "Show execution" button, hiding the approval prompt with no panels open.

**Decisions made (2026-08-08):** (1) fix via a **modal** approval prompt, not auto-closing the side panels; (2) the opt-out is per-user **with an admin override** (an admin-managed global/role setting that forces approval even for users who opted out); (3) the approval prompt stays **active by default** (users must explicitly opt out).

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Modal approval prompt (visibility fix)** — render `PlanPreview` inside a centered modal overlay with a z-index above the Activity/Chats panels (currently `20`), shown whenever a plan is awaiting approval/run; also auto-expand a collapsed Execution column on submit so the prompt and later progress are always visible. Keep the existing plan preview in the Execution column as the in-context summary. | The approval request is the only blocking step in chat; hiding it reads as "nothing is happening" and generates confusion/frustration. | ~0.5 day | Proposed |
| 2 | **Server-side settings foundation** — new `app_settings` (global, admin-managed) and `user_settings` (per-user) tables + idempotent migration + `init.sql`; singleton `SettingsService` with typed accessors and caching; API `GET/PUT /api/settings` (admin) and `GET/PUT /api/settings/me` (authenticated). | There is currently no settings infrastructure — only localStorage state services; "manage settings as an admin task" requires a server-side home. | ~1–1.5 days | Proposed |
| 3 | **Chat approval preference (first setting)** — `copilot.requireApproval`, per-user, **default true**; a "Don't ask again" checkbox on the approval modal (item 1) writes the preference; when off, plans auto-approve and execute immediately after planning (progress + cancel still visible in the Execution column). | Approval currently requires a Run click for every plan; users want a first-class opt-out that is editable, not buried. | ~0.5–1 day | Proposed |
| 4 | **Admin override for approval** — an admin-managed global/role setting that forces the approval prompt even for users who opted out (per decision #2), so admins can gate expensive corpus-wide operations for designated roles regardless of user preference. | The approval gate exists to stop expensive/unexpected runs; a user opt-out must not bypass an admin policy. | ~0.5 day | Proposed |
| 5 | **Admin Settings page** — `/settings` page gated to the Administrator role (same pattern as Governance), listing global settings with edit controls, plus an admin-only NavMenu entry; users see their own editable preferences. | Gives admins a first-class surface to manage settings once the foundation (item 2) exists. | ~0.5–1 day | Proposed |

## Suggested Sequencing

- **Item 1 first** — standalone, self-contained UX fix that can ship without the settings system.
- **Items 2 + 3 + 4 in one pass** — item 3 and 4 both consume the item 2 foundation, and the approval preference is the reference first setting.
- **Item 5 after 2–4** — the admin surface has nothing to manage until settings exist.

**Total (agent):** ~3–4 working days including build/test verification and Docker smoke, excluding the admin Settings page polish.
