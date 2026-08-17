# Backlog: Copilot Knowledge Panel Collapsed by Default

**Status:** Complete — items 1 + 2 implemented (Sprint 80, 2026-08-17). Archived.
**Created:** 2026-08-17
**Source:** Project-owner request (2026-08-17) — on the Chat (Copilot) page, the **Knowledge** panel for selecting the theme gets automatically opened when **New Chat** is pressed. Owner wants it to stay **not expanded** and only expand to select the theme when the **Knowledge** button is pressed, as the default behaviour; while no theme is selected, assume **all themes**.

## Problem

- **The Knowledge (theme) picker auto-opens on "New chat" and on a fresh session load.** `Pages/Copilot/Index.razor` sets `_showThemePicker = true` in two places that are not the Knowledge button: `ResetChatAsync` (the **New chat** handler, ~line 772) and `OnAfterRenderAsync` (a Sprint 58 discoverability heuristic that auto-opens the picker for a fresh empty session, ~lines 249–254). So every time a user starts a new conversation the theme picker forces itself open, covering the conversation start even when the user has no intention of scoping by theme.
- **The picker should be collapsed until asked for.** The default should be closed; the **Knowledge** button (`OpenThemePickerAsync`, already correct) and the chips **Edit** button should be the only ways to expand it. The Sprint 58 auto-open predates this preference and should be removed.

## Decisions (proposed approach)

1. **Collapsed by default — remove both auto-opens.** Delete the `_showThemePicker = true;` in `ResetChatAsync` and the fresh-session auto-open block in `OnAfterRenderAsync`. The field `private bool _showThemePicker;` already defaults to `false`, so the picker is collapsed unless `OpenThemePickerAsync` (Knowledge button / chips Edit) sets it true.
2. **No theme selected = all themes (already the semantics — lock it down).** `ApplyThemePickerAsync` maps an empty `_selectedThemes` to an empty `_session.ThemeFilter`, and `SendChat` passes `null` (all themes) when `ThemeFilter` is empty — the backend treats null source scope as all sources. This contract stays; a binding test documents it.
3. **Web-only.** No API, backend, or schema changes. No persisted-state change (the picker open/closed state is page-local and already not persisted).

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Remove both auto-opens** — drop `_showThemePicker = true;` from `ResetChatAsync` and the Sprint 58 fresh-session auto-open block in `OnAfterRenderAsync`; only `OpenThemePickerAsync` expands the picker. | The explicit ask: the Knowledge panel stays collapsed by default and opens only on the Knowledge button. | ~0.25 day | Proposed |
| 2 | **Tests + docs** — `CopilotIndexBindingTests`: picker stays collapsed until the Knowledge button is pressed (method-body scoped: `ResetChatAsync`/`OnAfterRenderAsync` no longer touch `_showThemePicker`; `OpenThemePickerAsync` still sets it true; field declared without an initializer) + no-theme-selected = all-themes contract (`ApplyThemePickerAsync` empty→empty `ThemeFilter`; `SendChat` empty→null); AGENTS/CLAUDE/File 02/03 + sprint file; backlog item archived. | The default-collapsed + all-themes contract is locked down. | ~0.25 day | Proposed |

## Suggested Sequencing

- **Items 1 + 2 together** — one small Copilot page edit + binding tests.

**Total (agent):** ~0.5 working day including build/test verification — a single small sprint.

## Out of Scope

- Changing theme-filter semantics on the backend or Search Center — "empty = all themes" already holds; only Copilot's default picker visibility changes.
- Persisting the picker open/closed state across reloads (it is page-local and stays so).
- Any change to the theme chips row (shown only when a theme IS selected) or the "All themes" / Apply / Cancel picker actions.
- Any API, backend, or schema change.
