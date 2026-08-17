# Sprint 80 - Copilot Knowledge Panel Collapsed by Default

**Status:** Active (2026-08-17)

Full authority: this file. Sprint 79 (Graph Orphan-Nodes Toggle) is **complete, committed, and pushed** on `origin/master` (`de055c0`).

Promotes `docs/backlog/Copilot-Knowledge-Panel-Collapsed-by-Default.md` — the project-owner-directed **Web-only** Copilot behavior fix: the **Knowledge** (theme) panel auto-opens on **New Chat** and on a fresh session load; the owner wants it to stay collapsed and only expand when the **Knowledge** button is pressed, and while no theme is selected to assume **all themes**.

## Objective

One small Web-only Copilot page pass (no API/backend changes, no schema migration):

1. **Collapsed by default.** Remove the two non-button auto-opens of `_showThemePicker` — the `_showThemePicker = true;` in `ResetChatAsync` (the **New chat** handler) and the Sprint 58 fresh-session auto-open block in `OnAfterRenderAsync`. The field already defaults to `false`, so the picker is collapsed unless `OpenThemePickerAsync` (the **Knowledge** button / chips **Edit**) sets it true.
2. **No theme selected = all themes (lock the contract).** `ApplyThemePickerAsync` maps an empty `_selectedThemes` to an empty `_session.ThemeFilter` and `SendChat` passes `null` when `ThemeFilter` is empty — backend treats null source scope as all sources. This existing semantics is documented + binding-tested, not changed.
3. **Docs.** AGENTS, CLAUDE, File 02/03, this sprint file; backlog item archived when complete.

## Decisions (from the backlog item, settled 2026-08-17)

1. **Collapsed by default — remove both auto-opens.** Delete `_showThemePicker = true;` from `ResetChatAsync` and the fresh-session auto-open block in `OnAfterRenderAsync`. `private bool _showThemePicker;` already defaults to `false`, so only `OpenThemePickerAsync` expands the picker.
2. **Empty selection = all themes** (already the semantics). `ApplyThemePickerAsync`: `_session.ThemeFilter = _selectedThemes.Count == 0 ? new List<string>() : _selectedThemes.ToList()`. `SendChat`: `_session.ThemeFilter is { Count: > 0 } ? _session.ThemeFilter : null`. Both stay; a binding test documents them.
3. **Web-only.** No API, backend, schema, or persisted-state changes (the picker open/closed state is page-local and already not persisted).

## Deliverables

### 1. Remove both auto-opens (`src/Aletheia.Web/Pages/Copilot/Index.razor`)
- `OnAfterRenderAsync`: delete the Sprint 58 block —
  ```csharp
  if (_session.Messages.Count == 0)
  {
      _showThemePicker = true;
  }
  ```
  (and its `// Sprint 58: ...` comment). The picker stays collapsed on a fresh session load.
- `ResetChatAsync`: delete `_showThemePicker = true;` (the **New chat** handler no longer opens the picker).
- `OpenThemePickerAsync` (the **Knowledge** button + chips **Edit** handler) keeps `_showThemePicker = true;` — the only expand path.

### 2. Tests (`tests/Aletheia.Web.UnitTests/CopilotIndexBindingTests.cs`)
- `Copilot_theme_picker_stays_collapsed_until_knowledge_button_is_pressed` — method-body scoped asserts on the razor source: `OpenThemePickerAsync` body still sets `_showThemePicker = true`; `ResetChatAsync` body and `OnAfterRenderAsync` body no longer reference `_showThemePicker`; the field is declared without an initializer (`private bool _showThemePicker;` → false).
- `Copilot_no_theme_selected_assumes_all_themes` — `ApplyThemePickerAsync` maps empty selection to empty `ThemeFilter` (`_session.ThemeFilter = _selectedThemes.Count == 0`), `SendChat` passes `null` when `ThemeFilter` is empty (`? _session.ThemeFilter : null`), and the **Knowledge** button markup is present (`>Knowledge</button>`, `title="Scope this session to knowledge themes"`).

### 3. Docs
- AGENTS (new Sprint 80 section), CLAUDE (Current state), File 02/03, this sprint file; backlog item archived when complete.

## Acceptance Criteria

- Pressing **New chat** leaves the Knowledge panel **collapsed**; a fresh session load leaves it collapsed too.
- The **Knowledge** button (and the chips **Edit** button, when a theme is already selected) still opens the picker.
- With nothing selected, the session assumes **all themes** (empty `ThemeFilter` → `null` scope on the chat request).
- Repository + Web + RAGS + Foundation unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Changing theme-filter semantics on the backend or Search Center ("empty = all themes" already holds; only Copilot's default picker visibility changes).
- Persisting the picker open/closed state across reloads (page-local, stays so).
- The theme chips row (shown only when a theme IS selected), the "All themes" / Apply / Cancel picker actions, or the picker styling.
- Any API, backend, or schema change.

---

## Implementation Status

**Implemented (2026-08-17).** All 3 items complete; tests green.

### Item 1 — Remove both auto-opens
- `Pages/Copilot/Index.razor`: `OnAfterRenderAsync` no longer auto-opens the picker for a fresh empty session (the Sprint 58 block + comment removed); `ResetChatAsync` (the **New chat** handler) no longer sets `_showThemePicker = true`. `OpenThemePickerAsync` (the **Knowledge** button + chips **Edit** handler) remains the only expand path. The field `private bool _showThemePicker;` still defaults to `false`.

### Item 2 — Tests
- **Web 153 (+2)**: `CopilotIndexBindingTests.Copilot_theme_picker_stays_collapsed_until_knowledge_button_is_pressed` (method-body scoped: `OpenThemePickerAsync` sets `_showThemePicker = true`; `ResetChatAsync` + `OnAfterRenderAsync` no longer reference `_showThemePicker`; field declared without an initializer) + `Copilot_no_theme_selected_assumes_all_themes` (`_session.ThemeFilter = _selectedThemes.Count == 0` in `ApplyThemePickerAsync`; `? _session.ThemeFilter : null` in `SendChat`; **Knowledge** button markup present). Web count 151 → 153.
- Foundation 55 / Repository 172 / RAGS 369 unchanged; build 0 errors; docs updated; backlog item archived.

**Residual manual (user-side):** `docker compose up -d --build`, then hard-refresh `/copilot` — the Knowledge panel stays collapsed on load and after **New chat**; press **Knowledge** (or chips **Edit**) to open it. With nothing selected the session uses all themes. No schema migration — Web-only.
