# Sprint 78 - Right Rail Strip Width (narrow the collapsed Activity/Chats strips)

**Status:** Active (2026-08-16)

Full authority: this file. Sprint 77 (AI Agent Instructions by Role) is **complete, committed, and pushed** on `origin/master` (`b886a73` + post-sprint fix `44c02c3`).

Promotes `docs/backlog/Right-Rail-Strip-Width.md` — the project-owner-directed **Web-only** follow-up to Sprint 75: the collapsed right-rail strip that holds the **Activity** and **Chats** toggle buttons is too wide. The buttons are shown vertically (`writing-mode: vertical-rl`), so their content is only ~22px wide (20px icon + 2px border), yet the strip reserves 42px. The extra ~18px eats into `main` and hides a button on the main panel — only a few characters on its left are readable.

## Objective

One small Web-only CSS pass (no API/backend changes, no schema migration):

1. **Narrow the collapsed strip.** Reduce the collapsed width from 42px to **24px** — just a few pixels more than the button content (20px icon + 2px border + 2px breathing room) — in both panel CSS files and both toggle buttons. The open state (420px) and the responsive overlay fallback are unchanged.
2. **Keep the two panels in sync.** Both panels hard-code the same collapsed width; the binding test asserts both use the same value.
3. **Tests + docs.** Update `RightRailBindingTests` (assert the narrow width in both panels; fix the "42px" doc comment) and the docs (AGENTS Sprint 75 section, CLAUDE, File 02/03, this sprint file); archive the backlog item when complete.

## Decisions (from the backlog item, settled 2026-08-16)

1. **Collapsed width 42px → 24px.** The toggle content is a 20px icon + 2px border = 22px; 24px leaves 2px of breathing room. The vertical label (~12.5px) and the count badge (min-width 20px) both fit. The `.right-rail` width is driven by the widest panel, so the rail itself narrows to 24px when both panels are collapsed.
2. **Literal value in both panel CSS files** (matching the existing style), with a binding test asserting both panels use the same collapsed width so they cannot drift.
3. **Web-only.** No API, backend, or schema changes.

## Deliverables

### 1. Narrow the collapsed strip (`Layout/ActivityPanel.razor.css`, `Layout/ChatsPanel.razor.css`, `Layout/MainLayout.razor.css`)
- `.activity-panel` / `.chats-panel`: `width: 42px` → `width: 24px`.
- `.activity-toggle` / `.chats-toggle`: `flex: 0 0 42px; width: 42px` → `flex: 0 0 24px; width: 24px`.
- `MainLayout.razor.css` `.right-rail` comment: "42px collapsed / 420px open" → "24px collapsed / 420px open".
- Open state (`.open` `width: 420px; flex: 1 1 auto`) and the `@media (max-width: 640.98px)` overlay fallback are untouched.

### 2. Binding tests (`tests/Aletheia.Web.UnitTests/RightRailBindingTests.cs`)
- New test: both panels' CSS use the same narrow collapsed width (`width: 24px` + `flex: 0 0 24px` in both `ActivityPanel.razor.css` and `ChatsPanel.razor.css`).
- Update the class doc comment: "Collapsed = a 42px vertical icon strip" → "a 24px vertical icon strip".

### 3. Docs
- AGENTS Sprint 75 section ("42px collapsed" → "24px collapsed"), CLAUDE, File 02/03, this sprint file; backlog item archived when complete.

## Acceptance Criteria

- The collapsed Activity/Chats strip is 24px wide — just a few pixels more than the button content — and no longer hides the main-panel button (its full label is readable).
- The open state (420px) and the responsive overlay fallback are unchanged.
- Both panels use the same collapsed width (binding test).
- Repository + Web + RAGS + Foundation unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Changing the open panel width (420px), the panel content, the Activity log data flow, job polling, or the Chats conversation restore flow.
- The responsive overlay fallback below the breakpoint.
- Any API, backend, or schema change.
- If a main-panel button is still clipped after the strip narrows (its container does not shrink), that is a separate layout follow-up.

---

## Implementation Status

**Implemented (2026-08-16).** All 3 items complete; tests green.

### Item 1 — Narrow the collapsed strip
- `Layout/ActivityPanel.razor.css` + `Layout/ChatsPanel.razor.css`: `.activity-panel`/`.chats-panel` `width: 42px` → `24px`; `.activity-toggle`/`.chats-toggle` `flex: 0 0 42px; width: 42px` → `flex: 0 0 24px; width: 24px`. The toggle content is a 20px icon + 2px border = 22px; 24px leaves 2px of breathing room. The vertical label (~12.5px) and the count badge (min-width 20px) both fit.
- `MainLayout.razor.css` `.right-rail` comment: "42px collapsed / 420px open" → "24px collapsed / 420px open". The `.right-rail` width is driven by the widest panel, so the rail itself narrows to 24px when both panels are collapsed.
- Open state (`.open` `width: 420px; flex: 1 1 auto`) and the `@media (max-width: 640.98px)` overlay fallback are untouched.

### Item 2 — Binding tests
- **Web 145 (+1)**: `RightRailBindingTests.Collapsed_strips_use_the_same_narrow_width` — both `ActivityPanel.razor.css` and `ChatsPanel.razor.css` use `width: 24px` + `flex: 0 0 24px` (the two panels cannot drift). Class doc comment updated: "Collapsed = a 24px vertical icon strip".

### Item 3 — Docs
- AGENTS Sprint 75 section ("42px collapsed" → "24px collapsed"), CLAUDE, File 02/03, this sprint file updated; backlog item archived.

**Residual manual (user-side):** `docker compose up -d --build`, then hard-refresh any page — the collapsed Activity/Chats strips are now 24px (a few px more than the button content) and no longer hide the main-panel button. No schema migration — Web-only.
