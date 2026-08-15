# Backlog: UI List Scrolling and Collapsible Forms

**Status:** Implemented — all 4 items delivered (Sprint 74, 2026-08-15)
**Created:** 2026-08-15
**Source:** Project-owner UI pass (2026-08-15) — "there are some lists that when displayed and the listing is too long it force the user to scroll the whole panel but it should scroll the list... the 'Add concept' is permanently open, make sure that is collapsed and when you want to add a concept then it should expand, maybe the 'Add Concept' and 'Unmapped terms' should be in a tab control. apply this ideas to other pages with similar control layout."

## Problem

- **Long lists force the whole panel to scroll.** On several pages a list/table that outgrows the viewport pushes the surrounding panel (and the whole document) down instead of scrolling inside its own container. Examples: `/lexicon` Concepts + Unmapped terms, `/glossary` Concepts + Verified facts, the `/wiki` index sidebar, `/governance` Roles/Audit/PII/Policies, `/taxonomy` Categories, `/ontology` Entities + Relationships. The user wants **the list to scroll, not the panel**.
- **The `/lexicon` "Add concept" form is permanently open.** The right column always renders the full Add/Edit form, so the page loads with a form open even when the admin only wants to browse concepts. It should be **collapsed by default and expand on demand**.
- **"Add concept" and "Unmapped terms" fight for the same column.** On `/lexicon` the form and the unmapped-terms list are stacked in one column, each unbounded. A tab control gives each its own space.

## Decisions (proposed approach)

1. **Scrollable-list convention.** A list/table container gets `max-height` + `overflow-y: auto` so a long list scrolls inside its panel while a short list keeps its natural height. One shared utility (`.list-scroll` / `.table-scroll` in `wwwroot/css/app.css`) is the single source of truth; the Wiki index sidebar is page-specific scoped CSS because it is a full-height grid sidebar.
2. **`/lexicon` right column becomes a tab control.** Two tabs: **Add concept** and **Unmapped terms**. The **Add concept form is collapsed by default** (the default active tab is Unmapped terms); clicking the Add concept tab (or **Edit** on a concept card) expands the form. While editing, the tab reads "Edit concept".
3. **Apply the same ideas to pages with a similar control layout** (a list/table beside other content): Glossary (Concepts + Verified facts), Wiki (index sidebar), Governance (Roles / Audit / PII matches / Policies), Taxonomy (Categories), Ontology (Entities + Relationships).
4. **Leave the reading-model and bounded surfaces alone:** paginated tables (Browse, Metadata picker), small bounded lists (Dashboard, Upload), primary-content lists where whole-page scroll is the intended model (Search Center results, Document viewer), and surfaces already height-constrained (Copilot, Graph Explorer).

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Scrollable-list utilities** — `.list-scroll` / `.table-scroll` (`max-height: 70vh; overflow-y: auto; overscroll-behavior: contain;`) in `app.css`. | One convention for "this list scrolls inside its panel". | ~0.25 day | Proposed |
| 2 | **`/lexicon` tab control + collapsible form** — Add concept / Unmapped terms tabs, Add concept collapsed by default, Edit auto-switches to the form tab, Concepts + Unmapped lists scrollable. | The explicit ask: the form stops being permanently open; long lists stop stretching the page. | ~0.5 day | Proposed |
| 3 | **Apply to similar pages** — Glossary (Concepts + facts table), Wiki (index sidebar max-height), Governance (Roles/Audit/PII/Policies), Taxonomy (Categories), Ontology (Entities + Relationships). | Every "list beside other content" panel gets independent scroll. | ~0.5 day | Proposed |
| 4 | **Tests + docs** — Web binding tests for the tab control, collapsed-by-default form, and the scroll utilities/page usage; AGENTS/CLAUDE/File 02/03 + sprint file; backlog item archived. | The UI contract is locked down. | ~0.5 day | Proposed |

## Suggested Sequencing

- **Items 1 + 2 + 3 together** — the whole change is one coherent UI pass: define the scroll convention, fix `/lexicon` (tabs + collapse + scroll), then apply the convention to the other list panels.
- **Item 4** alongside each item, not a trailing batch.

**Total (agent):** ~1.5–2 working days including build/test verification — a single sprint.

## Out of Scope

- Changing page routes, API contracts, or backend behavior — this is a Web-only UI pass (no schema migration).
- Re-architecting pages that already constrain their own scroll regions (Copilot, Graph Explorer).
- Making paginated or inherently bounded lists (Browse, Metadata picker, Dashboard, Upload) scroll — they cannot outgrow their panel by design.
- Turning Search Center's result list or the Document viewer into internal scroll regions — whole-page scroll is their intended reading model.
- Bootstrap collapse/tab JS widgets — the tab/collapse behavior is Blazor state (conditional render), testable without JS interop.
