# Sprint 74 - UI List Scrolling and Collapsible Forms

**Status:** Active (2026-08-15)

Full authority: this file. Sprint 73 (Ingestion Guard-Rails and Summaries Readability) is **complete, committed, and pushed** on `origin/master` (`1cdb2d8`).

Promotes `docs/backlog/UI-List-Scroll-and-Collapsible-Forms.md` — the project-owner-directed UI pass from the 2026-08-15 session: long lists should scroll inside their own panel instead of forcing the whole panel to scroll, and the `/lexicon` "Add concept" form should stop being permanently open (collapsed by default, expand on demand — organized as an Add concept / Unmapped terms tab control). The ideas then apply to every page with a similar control layout.

## Objective

One coherent Web-only UI pass (no API/backend changes, no schema migration):

1. **Lists scroll inside their panel.** A list/table that outgrows the viewport scrolls within its own container (`max-height` + `overflow-y: auto`) instead of stretching the surrounding panel and the page. Applied to every "list beside other content" surface: `/lexicon` Concepts + Unmapped terms, `/glossary` Concepts + Verified facts, `/wiki` index sidebar, `/governance` Roles / Audit / PII matches / Policies, `/taxonomy` Categories, `/ontology` Entities + Relationships.
2. **The `/lexicon` "Add concept" form is collapsed by default.** The right column becomes an **Add concept / Unmapped terms** tab control. The form is hidden until the admin clicks the **Add concept** tab (or **Edit** on a concept card, which auto-opens the form for editing). No permanently-open form.

## Decisions (from the backlog item, settled 2026-08-15)

1. **One scroll convention.** `.list-scroll` / `.table-scroll` utilities in `wwwroot/css/app.css` (`max-height: 70vh; overflow-y: auto; overscroll-behavior: contain;`) — a short list keeps natural height, a long list scrolls internally. The Wiki index sidebar uses page-scoped CSS because it is a full-height grid sidebar.
2. **`/lexicon` right column = tab control, form collapsed by default.** Default active tab: **Unmapped terms** (the form is collapsed on load). Clicking **Add concept** (or **Edit** on a card) activates the form tab; while editing the tab reads "Edit concept". Tab switching is Blazor state (conditional render), not Bootstrap collapse/tab JS.
3. **Apply to similar control layouts.** Glossary (two-column lists), Wiki (sidebar index), Governance (stacked list cards), Taxonomy / Ontology (list + detail columns). Leave reading-model and bounded surfaces alone: paginated tables (Browse, Metadata picker), small bounded lists (Dashboard, Upload), Search Center results + Document viewer (whole-page scroll is the intended model), Copilot + Graph Explorer (already height-constrained).

## Deliverables

### 1. Scrollable-list utilities (`app.css`)
- `.list-scroll` and `.table-scroll` — `max-height: 70vh; overflow-y: auto; overscroll-behavior: contain;`. `.table-scroll` composes with Bootstrap's `table-responsive` (which already handles `overflow-x`) on the same element.

### 2. `/lexicon` tab control + collapsible Add concept (`Pages/Lexicon/Index.razor`)
- Right column becomes a `nav-tabs` tab control with **Add concept** / **Unmapped terms** tabs.
- Default active tab: **Unmapped terms** — the Add/Edit form is collapsed on page load.
- `EditConceptAsync` auto-activates the form tab (and sets `_editingKey`); the tab label flips to "Edit concept" while editing.
- Concepts list and Unmapped terms list each wrapped in `.list-scroll` so long lists scroll inside the column.

### 3. Apply to similar pages
- **Glossary** (`Pages/Glossary/Index.razor`): Concepts cards wrapped in `.list-scroll`; Verified facts table wrapper gets `table-responsive table-scroll`.
- **Wiki** (`Pages/Wiki.razor.css`): `.wiki-index` gains `max-height: calc(100vh - 16rem); overflow-y: auto;` — the index sidebar scrolls independently of the article.
- **Governance** (`Pages/Governance/Index.razor`): Roles / PII matches / Policies lists wrapped in `.list-scroll`; Audit log table wrapper gets `table-responsive table-scroll`.
- **Taxonomy** (`Pages/TaxonomyExplorer.razor`): Categories list wrapped in `.list-scroll`.
- **Ontology** (`Pages/OntologyExplorer.razor`): Entities list wrapped in `.list-scroll`; Relationships table wrapped in `table-responsive table-scroll`.

### 4. Tests + docs
- **Web** binding tests: `LexiconBindingTests` gains the tab-control, collapsed-by-default, and Edit-switches-to-form-tab assertions; new `ListScrollBindingTests` asserts the `app.css` utilities and each page's use of them (including the Wiki scoped CSS).
- AGENTS, CLAUDE, File 02/03, this sprint file; backlog item archived.

## Acceptance Criteria

- On `/lexicon`, the Add concept form is **not** visible on page load; the admin expands it via the **Add concept** tab or by clicking **Edit** on a concept (which pre-fills the form). Unmapped terms live in their own tab.
- A long Concepts / Unmapped / Glossary / Governance / Taxonomy / Ontology / Wiki-index list scrolls inside its panel; a short list keeps its natural height (no wasted empty scroll area).
- No API, backend, or schema changes — Web-only.
- Repository + Web + RAGS + Foundation unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Bootstrap collapse/tab JS widgets (tab/collapse behavior is Blazor state — conditional render, testable without JS interop).
- Re-architecting pages that already constrain their own scroll regions (Copilot, Graph Explorer).
- Making paginated or bounded lists scroll (Browse, Metadata picker, Dashboard, Upload).
- Turning Search Center results or the Document viewer into internal scroll regions (whole-page scroll is their reading model).
- Any backend, controller, or schema change.

---

## Implementation Status

**Implemented (2026-08-15).** All 4 items complete; tests green.

### Item 1 — Scrollable-list utilities
- `wwwroot/css/app.css` gains `.list-scroll` and `.table-scroll` (`max-height: 70vh; overflow-y: auto; overscroll-behavior: contain;`). `.table-scroll` composes with `table-responsive`.

### Item 2 — `/lexicon` tab control + collapsible Add concept
- `Pages/Lexicon/Index.razor` right column is now a `nav-tabs` control: **Add concept** / **Unmapped terms**. Default tab = Unmapped terms → the form is collapsed on load. `EditConceptAsync` sets `_activeTab = LexiconTab.AddConcept`; the tab label reads "Edit concept" while `_editingKey` is set. Concepts + Unmapped lists wrapped in `.list-scroll`.

### Item 3 — Apply to similar pages
- Glossary: Concepts cards in `.list-scroll`; facts table wrapper `table-responsive table-scroll`.
- Wiki: `.wiki-index` gains `max-height: calc(100vh - 16rem); overflow-y: auto;`.
- Governance: Roles/PII/Policies lists in `.list-scroll`; Audit table `table-responsive table-scroll`.
- Taxonomy: Categories in `.list-scroll`.
- Ontology: Entities in `.list-scroll`; Relationships table `table-responsive table-scroll`.

### Item 4 — Tests + docs
- **Web 125 (+10)**: `LexiconBindingTests` (+4 — tab control, collapsed-by-default, Edit switches to the form tab, lists use `.list-scroll`) and new `ListScrollBindingTests` (+6 — `app.css` utilities defined with `overflow-y`/`max-height`; Glossary/Governance/Taxonomy/Ontology use them; Wiki scoped CSS scrolls `.wiki-index`).
- Foundation 55 / Repository 157 / RAGS 361 unchanged; `dotnet build Aletheia.slnx` succeeds (0 errors). Docs updated; backlog item archived.

**Residual manual (user-side):** `docker compose up -d --build`, then hard-refresh `/lexicon` (tabs; Add concept collapsed by default; long lists scroll in place) and the other surfaces (`/glossary`, `/wiki`, `/governance`, `/taxonomy`, `/ontology`) for a live visual check. No schema migration — Web-only.

### Post-sprint (2026-08-15): one-click Promote in Unmapped terms

- Each pending unmapped term row in `/lexicon` gains a **Promote** button next to **Dismiss**: it upserts a concept (`Key` = `Label` = the term, `ValuePattern = "text"`) and resolves the pending record in a single action — the term graduates from unmapped to canonical; refine value pattern/scope/aliases afterwards via **Edit** on the concept card.
- The `_message` alert moved above the tab content so Promote/Dismiss feedback shows on both tabs (previously it only rendered inside the Add-concept form).
- The Concepts list on `/lexicon` **and** `/glossary` was already scrollable from Items 2 + 3 (`.list-scroll` wraps both) — confirmed, no further change needed.
- Web 127 (+2): `Lexicon_unmapped_terms_have_a_promote_button` + `Lexicon_promote_upserts_a_concept_and_resolves_the_term` (`LexiconBindingTests`). Build 0 errors.
