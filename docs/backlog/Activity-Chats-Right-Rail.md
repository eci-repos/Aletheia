# Backlog: Activity/Chats Right Rail (in-flow, no overlap)

**Status:** Proposed (not yet authorized — waiting for a sprint to promote it)
**Created:** 2026-08-15
**Source:** Project-owner request (2026-08-15) — the right-side **Activity** and **Chats** panels "block the view of visual artifacts behind"; keep them where they are but make the main panel items not overlap, leaving a clean vertical strip for the side tabs. Owner confirmed: **Option A** — the panels collapse into vertical strips with related icons + count badges, and expand to push content. Starts **collapsed by default**.

## Problem

- **The Activity and Chats panels float over page content.** `Layout/ActivityPanel.razor` and `Layout/ChatsPanel.razor` (rendered in `Layout/MainLayout.razor`) are `position: fixed; right: 0;` overlays (Activity `z-index: 20`, Chats `z-index: 21`). When open they expand to `width: min(420px, calc(100vw - 24px))` and cover whatever is behind them — graphs, tables, cards. There is no reserved layout space for them, so content is never aware of the panels.
- **No precise, clean way to keep the tabs visible without overlap.** The collapsed state (a 42px toggle strip) is the only non-overlapping form, but the open panel always obscures visuals.

## Decisions (proposed approach)

1. **In-flow right rail (Option A).** Move both panels out of `position: fixed` into the page layout as a right column beside `<main>` in `MainLayout.razor` — mirroring the existing left `NavMenu` sidebar. Collapsed = a vertical icon strip; open = expands to ~420px and **pushes** the main content instead of covering it. No overlap in any state; the tabs live in their own clean vertical strip.
2. **Collapsed by default, icons + counts on the strip.** Both panels start collapsed (`isOpen = false`, as today). The collapsed strip shows related icons per the existing `nav-icon icon-*` convention in `NavMenu.razor` (add `icon-activity` + `icon-chats`) plus count badges: the Activity running-count badge (`activity-count`) is retained, and Chats gains a count of open conversations. The at-a-glance counts stay visible in the collapsed state.
3. **Two stacked strips, not one shared rail.** Keep Activity and Chats as two independent vertically-stacked strips (Activity above, Chats below), preserving the current mental model and independent toggles.
4. **Responsive fallback.** Below a breakpoint, the rail falls back to a full-height overlay (current behavior) so narrow screens are not crushed.
5. **Web-only.** No API, backend, or schema changes.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **In-flow right rail** — `MainLayout.razor` gains a right column (42px collapsed / ~420px open, pushes content); `ActivityPanel`/`ChatsPanel` CSS drops `position: fixed` for grid/flex layout with the resize/responsive behavior. | Panels stop covering content; the tabs get a clean vertical strip. | ~0.5 day | Proposed |
| 2 | **Collapsed strip icons + count badges** — `icon-activity` / `icon-chats` (NavMenu `nav-icon` convention), Activity running badge retained, Chats open-conversation count added. | At-a-glance counts in the collapsed strip (owner's explicit ask). | ~0.25 day | Proposed |
| 3 | **Responsive fallback + tests + docs** — below the breakpoint return to overlay; Web binding tests (MainLayout renders the panels in the layout, collapsed by default, icon + count classes present); AGENTS/CLAUDE/File 02/03 + sprint file; backlog item archived. | Contract locked down + no layout regression on small screens. | ~0.5 day | Proposed |

## Suggested Sequencing

- **Items 1 + 2 together** — one coherent layout pass (the rail and its strip affordances are the same change).
- **Item 3** alongside each item, not a trailing batch.

**Total (agent):** ~1–1.25 working days including build/test verification — a single sprint.

## Out of Scope

- Changing panel content, the Activity log data flow, job polling, or the Chats conversation restore flow — this is a layout/positioning change only.
- Merging Activity and Chats into a single shared rail or tab control (decision 3 keeps them as two independent stacked strips).
- Any API, backend, or schema change.
- Changing the left sidebar layout or the Copilot page's own internal three-column grid (`.copilot-layout`).
