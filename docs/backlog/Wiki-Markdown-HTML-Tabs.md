# Backlog: Wiki Markdown / HTML View Tabs

**Status:** **Proposed** — not yet promoted to a sprint. No work authorized.
**Created:** 2026-08-13
**Source:** Design review with the project owner. Wiki pages are stored and returned as markdown in `WikiPage.Summary`, but the Wiki surface (`src/Aletheia.Web/Pages/Wiki.razor:151`) renders it as a plain `<p>` — so users literally see raw markdown syntax (e.g. `## Heading`, `- bullet`) as unformatted text. Users who don't read markdown find this hard to parse. The fix is a tab control: **View** (markdown rendered to styled HTML) and **Source** (raw markdown in a `<pre>`). RTF was considered and rejected — browsers cannot natively render RTF, so it would be a worse experience than the md.

## Problem

- Wiki pages show raw markdown markup as plain text, which is hard to read for non-technical users.
- Copilot already has a working mini renderer (`RenderMarkdown` at `Pages/Copilot/Index.razor:923`, handles headings/tables/lists/paragraphs), but it's page-local and unused by Wiki.

## Decisions made (2026-08-13)

1. **The "friendly" view is markdown rendered to HTML**, not RTF. RTF is a legacy desktop format browsers can't render inline; rendered HTML is the standard, zero-new-dependency answer.
2. **Reuse the existing Copilot renderer, extracted to a shared helper**, rather than adding Markdig. Simpler, no new package, consistent styling between Copilot and Wiki. Markdig stays a future option if richer markdown coverage (GFM links, code fences) is ever needed.
3. **Two tabs: "View" (default) and "Source".** Source shows the raw `Summary` markdown read-only in a `<pre>`; View shows it rendered. Tab choice is ephemeral UI state (a field on the page), not persisted or wired through the API — pure presentation, no wire/API/DTO changes.
4. **Editing is unchanged** — the existing edit textarea already works on raw markdown, so editors still get the md.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Shared markdown renderer** — move Copilot's `RenderMarkdown` (+ its table/list/heading helpers) from `Pages/Copilot/Index.razor` into a shared static helper (e.g. `Services/MarkdownRenderer.cs`); update Copilot to call it. Unit-test the helper. | One renderer, one style; Wiki and Copilot stay consistent instead of two drifting copies. | ~0.5 day | Proposed |
| 2 | **Wiki view tabs** — add a View/Source tab toggle to `Wiki.razor`; View renders `_selectedPage.Summary` through the shared helper, Source shows it in a read-only `<pre>`. Default View. CSS in `Wiki.razor.css` (reuse existing `wiki-*` classes where possible). | This is the user-facing payoff: readable pages by default, raw markdown available for power users. | ~0.5 day | Proposed |
| 3 | **Tests + docs** — renderer unit tests (headings/tables/lists/empty/null input, HTML escaping); Wiki page binding tests for tab switching; docs (Architecture, user guide, AGENTS, File 02/03, sprint file when promoted). | Renderer correctness is a security/display claim — must escape raw HTML from source content. | ~0.25–0.5 day | Proposed |

## Suggested Sequencing

- **Item 1 first** — the shared renderer is the prerequisite; Copilot's call sites update in the same pass.
- **Item 2 after 1** — the tabs consume the shared renderer.
- **Item 3 alongside each** — renderer tests with 1, tab tests with 2.

**Total (agent):** ~1–1.5 working days including build/test verification. No API, schema, or wire changes.

## Out of Scope

- RTF/PDF/any export or download format for wiki pages.
- Markdig or full GFM support (headings/tables/lists/paragraphs only — what the shared helper already does).
- Persisting a per-user default tab or wiring the tab through the API/DTOs.
- Rendering links/images embedded in wiki summaries as interactive elements (paragraph/heading/table/list only, matching Copilot today).
