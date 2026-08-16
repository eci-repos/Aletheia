# Sprint 75 - Activity and Chats Right Rail (in-flow, no overlap)

**Status:** Active (2026-08-16)

Full authority: this file. Sprint 74 (UI List Scrolling and Collapsible Forms) is **complete, committed, and pushed** on `origin/master` (`343d9ca` + post-sprint `d8cb518`).

Promotes `docs/backlog/Activity-Chats-Right-Rail.md` — the project-owner-directed layout pass from the 2026-08-15 session: the right-side **Activity** and **Chats** panels "block the view of visual artifacts behind"; keep them where they are but make the main panel items not overlap, leaving a clean vertical strip for the side tabs. Owner confirmed **Option A** — the panels collapse into vertical strips with related icons + count badges, and expand to **push** content. Starts **collapsed by default**.

## Objective

One coherent Web-only layout pass (no API/backend changes, no schema migration):

1. **In-flow right rail.** Move both panels out of `position: fixed` into the page layout as a right column beside `<main>` in `MainLayout.razor` — mirroring the existing left `NavMenu` sidebar. Collapsed = a vertical icon strip; open = expands to ~420px and **pushes** the main content instead of covering it. No overlap in any state.
2. **Collapsed by default, icons + counts on the strip.** Both panels start collapsed (`isOpen = false`, as today). The collapsed strip shows related icons per the existing `nav-icon icon-*` convention (add `icon-activity` + `icon-chats`) plus count badges: the Activity running-count badge (`activity-count`) is retained, and Chats keeps its open-conversation count (`chats-count`).
3. **Two stacked strips, not one shared rail.** Activity above, Chats below — two independent vertically-stacked strips preserving the current mental model and independent toggles.
4. **Responsive fallback.** Below the breakpoint, the rail falls back to a full-height overlay (current behavior) so narrow screens are not crushed.

## Decisions (from the backlog item, settled 2026-08-15)

1. **In-flow right rail (Option A).** `MainLayout.razor` gains a `<div class="right-rail">` right column between `<main>` and the end of `.page`; `ActivityPanel`/`ChatsPanel` drop `position: fixed` for flex layout. The rail is a flex column; each panel is a flex row (`[shell][toggle strip]`) with `align-self: flex-end` so the strip stays at the right edge when its sibling is open. The rail width is driven by the widest panel (42px collapsed / 420px open) — `main` (`flex: 1; min-width: 0`) shrinks to make room, so opening a panel **pushes** content.
2. **Collapsed strip = icon + vertical label + count badge.** The toggle button keeps its vertical `writing-mode` label ("Activity" / "Chats") and gains an icon span (`icon-activity` / `icon-chats`, the NavMenu `--icon` mask convention) above the label; the count badge (`activity-count` / `chats-count`) is retained below. Icons defined in `app.css` (global) so the panels can use them.
3. **Two stacked strips.** Activity panel above, Chats panel below, each `flex: 0 0 auto` collapsed and `flex: 1 1 auto` open (they split the rail height when both are open; a collapsed strip takes no vertical space).
4. **Responsive fallback.** Below `640.98px` the `.right-rail` becomes `position: fixed; top: 0; right: 0; bottom: 0; z-index: 30` (overlay, current behavior) and an open panel widens to `calc(100vw - 12px)`.
5. **Web-only.** No API, backend, or schema changes.

## Deliverables

### 1. In-flow right rail (`Layout/MainLayout.razor` + `MainLayout.razor.css`)
- `.page` keeps `display: flex; flex-direction: row`; add `<div class="right-rail">` wrapping `<ActivityPanel />` + `<ChatsPanel />` after `<main>`.
- `.right-rail` — `display: flex; flex-direction: column; flex: 0 0 auto; height: 100vh; position: sticky; top: 0;` (width driven by the widest panel).
- `main` already `flex: 1; min-width: 0` — opening a panel pushes content, never covers it.

### 2. Panels become in-flow flex rows (`Layout/ActivityPanel.razor` + `.razor.css`, `Layout/ChatsPanel.razor` + `.razor.css`)
- Each `<aside>` becomes `display: flex; flex-direction: row; flex: 0 0 auto; align-self: flex-end; width: 42px;` (collapsed) / `width: 420px; flex: 1 1 auto;` (open). The shell renders **before** the toggle in DOM (shell `flex: 1; min-height: 0`, toggle `flex: 0 0 42px`).
- The shell drops `position`-era `height: calc(...)` + `margin-right: 42px`; it fills the panel (`flex: 1; min-height: 0; overflow: hidden; display: flex; flex-direction: column;`), keeping its internal scrollable list.
- The toggle button keeps its vertical label + count badge and gains an icon span (`activity-toggle-icon icon-activity` / `chats-toggle-icon icon-chats`).

### 3. Strip icons (`wwwroot/css/app.css`)
- `.icon-activity` and `.icon-chats` — `--icon` mask URLs (feather-style, matching the NavMenu set). The panels' scoped `.activity-toggle-icon` / `.chats-toggle-icon` classes apply the mask (`background-color: currentColor; -webkit-mask: var(--icon) center / contain no-repeat; mask: ...`).

### 4. Responsive fallback (panel CSS `@media (max-width: 640.98px)`)
- `.right-rail` → `position: fixed; top: 0; right: 0; bottom: 0; z-index: 30;`; open panels widen to `calc(100vw - 12px)`.

### 5. Tests + docs
- **Web** binding tests: new `RightRailBindingTests` — MainLayout renders the panels inside a `.right-rail`; panels collapsed by default; `icon-activity`/`icon-chats` + `activity-count`/`chats-count` present; panel CSS is in-flow (no `position: fixed` on the base rule) with a `@media (max-width: 640.98px)` overlay fallback; `app.css` defines the two icons.
- AGENTS, CLAUDE, File 02/03, this sprint file; backlog item archived when complete.

## Acceptance Criteria

- On any page, the Activity and Chats panels never cover page content: collapsed they are a 42px vertical icon strip at the right edge; open they expand to ~420px and **push** the main content.
- Both panels start collapsed; the collapsed strips show an icon + count badge (Activity running count, Chats open-conversation count).
- Activity and Chats are two independent stacked strips (Activity above, Chats below) with independent toggles.
- Below the breakpoint the rail returns to a full-height overlay so narrow screens are not crushed.
- No API, backend, or schema changes — Web-only.
- Repository + Web + RAGS + Foundation unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Changing panel content, the Activity log data flow, job polling, or the Chats conversation restore flow — this is a layout/positioning change only.
- Merging Activity and Chats into a single shared rail or tab control (decision 3 keeps them as two independent stacked strips).
- Any API, backend, or schema change.
- Changing the left sidebar layout or the Copilot page's own internal three-column grid (`.copilot-layout`).

---

## Implementation Status

**Implemented (2026-08-16).** All 4 items complete; tests green.

### Item 1 — In-flow right rail
- `Layout/MainLayout.razor` gains `<div class="right-rail">` wrapping `<ActivityPanel />` + `<ChatsPanel />` after `<main>`; `MainLayout.razor.css` adds `.right-rail` (`display: flex; flex-direction: column; flex: 0 0 auto; height: 100vh; position: sticky; top: 0;`). `main` (`flex: 1; min-width: 0`) shrinks when a panel opens — content is pushed, never covered.

### Item 2 — Panels become in-flow flex rows
- `ActivityPanel.razor` / `ChatsPanel.razor`: the `<aside>` is now `display: flex; flex-direction: row; flex: 0 0 auto; align-self: flex-end;` — 42px collapsed, `width: 420px; flex: 1 1 auto;` open. The shell renders before the toggle (shell `flex: 1; min-height: 0`, toggle `flex: 0 0 42px`); the shell drops its `position`-era `height`/`margin-right` and fills the panel. The toggle keeps its vertical label + count badge and gains an icon span (`activity-toggle-icon icon-activity` / `chats-toggle-icon icon-chats`).

### Item 3 — Strip icons
- `wwwroot/css/app.css` defines `.icon-activity` (pulse) and `.icon-chats` (message bubble) `--icon` mask URLs; the panels' scoped `.activity-toggle-icon` / `.chats-toggle-icon` apply the mask.

### Item 4 — Responsive fallback
- Both panel CSS files: `@media (max-width: 640.98px)` makes `.right-rail` `position: fixed; top: 0; right: 0; bottom: 0; z-index: 30;` and open panels `width: calc(100vw - 12px)` — the rail returns to a full-height overlay on narrow screens.

### Item 5 — Tests + docs
- **Web 133 (+6)**: new `RightRailBindingTests` — MainLayout renders the panels inside a `.right-rail`; panels collapsed by default; `icon-activity`/`icon-chats` + `activity-count`/`chats-count` present; panel CSS is in-flow (no `position: fixed` on the base rule) with a `@media (max-width: 640.98px)` overlay fallback; `app.css` defines the two icons.
- Foundation 55 / Repository 157 / RAGS 361 unchanged; `dotnet build Aletheia.slnx` succeeds (0 errors). Docs updated; backlog item archived.

**Residual manual (user-side):** `docker compose up -d --build`, then hard-refresh any page (e.g. `/graph`, `/search`) — the Activity/Chats strips sit at the right edge as 42px icon strips; opening one pushes the content instead of covering it; both start collapsed. On a narrow window (< 641px) the rail returns to an overlay. No schema migration — Web-only.
