# Backlog: Right Rail Strip Width (narrow the collapsed Activity/Chats strips)

**Status:** **Proposed** — promoted to Sprint 78 (2026-08-16).
**Created:** 2026-08-16
**Source:** Project-owner request (2026-08-16) — the collapsed right-rail strip that holds the **Activity** and **Chats** toggle buttons is too wide. The buttons are shown vertically (`writing-mode: vertical-rl`), so their content is only ~22px wide (20px icon + 2px border), yet the strip reserves 42px. The extra ~18px eats into `main` and hides a button on the main panel — only a few characters on its left are readable.

## Problem

- **The collapsed strip is 42px wide but the buttons only need ~22px.** `Layout/ActivityPanel.razor.css` and `Layout/ChatsPanel.razor.css` set `.activity-panel`/`.chats-panel` to `width: 42px` and the toggle buttons to `flex: 0 0 42px; width: 42px`. The toggle content is a 20px icon + 2px border; the vertical label is ~12.5px wide. The strip is ~18px wider than its content.
- **The strip hides a main-panel button.** Because the rail is in-flow (Sprint 75), `main` (`flex: 1; min-width: 0`) is 42px narrower than it needs to be. A button near the right edge of the main content is clipped — only a few characters on its left are readable; the rest sits under the opaque white toggle strip.

## Decisions (proposed approach)

1. **Narrow the collapsed strip to just a few pixels more than the button content.** Reduce the collapsed width from 42px to **24px** (20px icon + 2px border + 2px breathing room) in both panel CSS files and both toggle buttons. The open state (420px) and the responsive overlay fallback are unchanged.
2. **Keep the two panels in sync.** Both panels hard-code the same collapsed width today; the binding test asserts both use the same value so they cannot drift.
3. **Web-only.** No API, backend, or schema changes.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Narrow the collapsed strip** — `.activity-panel`/`.chats-panel` `width: 42px` → `24px`; `.activity-toggle`/`.chats-toggle` `flex: 0 0 42px; width: 42px` → `flex: 0 0 24px; width: 24px`; update the `.right-rail` comment in `MainLayout.razor.css` ("42px collapsed" → "24px collapsed"). | The strip stops eating ~18px of `main`; the hidden main-panel button becomes fully readable. | ~0.25 day | Proposed |
| 2 | **Binding tests** — `RightRailBindingTests` asserts both panels use the same narrow collapsed width (`width: 24px` + `flex: 0 0 24px`); update the class doc comment ("42px" → "24px"). | Contract locked down; the two panels can't drift. | ~0.25 day | Proposed |
| 3 | **Docs** — AGENTS Sprint 75 section ("42px collapsed" → "24px collapsed"), CLAUDE, File 02/03, sprint file; backlog item archived when complete. | Standing docs mandate. | ~0.25 day | Proposed |

## Suggested Sequencing

- **Items 1 + 2 together** — the CSS change and its binding test are the same edit.
- **Item 3** alongside, not trailing.

**Total (agent):** ~0.5 working day including build/test verification — a single sprint.

## Out of Scope

- Changing the open panel width (420px), the panel content, the Activity log data flow, job polling, or the Chats conversation restore flow.
- The responsive overlay fallback below the breakpoint.
- Any API, backend, or schema change.
- If a main-panel button is still clipped after the strip narrows (its container does not shrink), that is a separate layout follow-up — the owner's ask is the strip width.
