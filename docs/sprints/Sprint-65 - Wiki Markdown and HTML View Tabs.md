# Sprint 65 - Wiki Markdown and HTML View Tabs

**Status:** Active (2026-08-13)

Full authority: this file. Sprint 64 (Theme-Aware Graph Retrieval) is **complete, committed, and pushed** (`9fd1355` is the backlog-item commit that preceded it; Sprint 64's implementation shipped in the `7d91bbf..` range).

Promotes `docs/backlog/Wiki-Markdown-HTML-Tabs.md` — the "friendly documentation viewer" request from the project owner: wiki pages are stored/returned as markdown in `WikiPage.Summary` but the Wiki surface renders them as a plain `<p>`, so users literally see raw markdown syntax as unformatted text.

## Objective

Give wiki pages a tab control: **View** (markdown rendered to styled HTML — headings, tables, lists, paragraphs, inline bold/code) and **Source** (the raw markdown read-only in a `<pre>`). The friendly view is rendered HTML, not RTF (browsers can't render RTF inline). Reuse the working mini-renderer Copilot already has (`RenderMarkdown` at `Pages/Copilot/Index.razor`), extracted to a shared helper so Wiki and Copilot share one renderer instead of drifting.

## Background

- `WikiPage.Summary` holds markdown; `Wiki.razor:151` prints it via `<p class="wiki-summary">@_selectedPage.Summary</p>` — Razor auto-escapes, so users see `## Heading` / `- bullet` literally.
- `Pages/Copilot/Index.razor` has a self-contained static `RenderMarkdown` (headings `#`–`####`, pipe tables with `---` separator row, `-`/`*` lists, paragraphs, inline `**bold**` and `` `code` ``), all HTML-encoded via `HtmlEncoder` before formatting. It also has a Copilot-specific special case: content that trims to a `{`/`[` JSON blob renders as a `<pre class="copilot-json">`.
- Web unit tests are **source-assertion tests** (read `.razor`/`.cs` text and `Assert.Contains`, no bUnit rendering) — see `tests/Aletheia.Web.UnitTests/CopilotIndexBindingTests.cs`.

## Deliverables

### 1. Shared markdown renderer
- New `src/Aletheia.Web/Services/MarkdownRenderer.cs` (static `MarkdownRenderer`): `ToHtml(string content)` returning an HTML string — the Copilot renderer logic verbatim (escape → headings/tables/lists/paragraphs → inline bold/code), minus the Copilot JSON special case.
- Emitted table classes renamed from `copilot-table-wrap`/`copilot-table` to neutral `md-table-wrap`/`md-table`; update `Pages/Copilot/Index.razor.css` accordingly.
- `Pages/Copilot/Index.razor`: `RenderMarkdown` keeps its JSON `<pre class="copilot-json">` branch, otherwise returns `new MarkupString(MarkdownRenderer.ToHtml(content))`; delete the moved private helpers and their now-unused `using` directives (`System.Text`, `System.Text.RegularExpressions`; keep `System.Text.Encodings.Web` for the JSON branch's `HtmlEncoder`).

### 2. Wiki View/Source tabs
- `Pages/Wiki.razor`: replace the `<p class="wiki-summary">` block with a tab bar (`View` / `Source`, `btn-group btn-group-sm`, default **View**) and the body:
  - View: `<div class="wiki-rendered">@((MarkupString)MarkdownRenderer.ToHtml(_selectedPage.Summary))</div>`
  - Source: `<pre class="wiki-source-view">@_selectedPage.Summary</pre>` (Razor auto-escapes the raw md).
- Tabs render only when not `_editing` (the editor textarea stays raw markdown). State is a private `_viewMode` field on the page — ephemeral UI state, no API/wire changes.
- `Pages/Wiki.razor.css`: `md-table-wrap`/`md-table` rules (match the Copilot values) + `.wiki-source-view` (pre-wrap) + tab-styling where needed.

### 3. Tests
- `tests/Aletheia.Web.UnitTests/MarkdownRendererTests.cs`: headings, tables, lists, paragraphs, inline bold/code, HTML escaping of `<script>`, empty/null → empty.
- Wiki tab binding tests (source-assertion): `Wiki.razor` contains the View/Source tab buttons, calls `MarkdownRenderer.ToHtml`, and renders the raw source in a `<pre>`; `Copilot/Index.razor` now calls `MarkdownRenderer.ToHtml` and no longer defines the duplicated table/heading/list helpers.
- Existing suites stay green (RAGS 289 / Repository 130 / Foundation 55 / Web 46).

### 4. Docs
- `docs/File 02-Current-Sprint.md` (this becomes the active sprint), AGENTS.md, CLAUDE.md, `docs/File 03-openhands.md`, this sprint file's Implementation Status, and the backlog item (moved to `docs/backlog/archive/` with its status header updated when complete).

## Acceptance Criteria

- A wiki page shows a View/Source toggle; View renders headings/tables/lists/bold/code from the page summary; Source shows the raw markdown escaped in a `<pre>`.
- Copilot chat rendering is unchanged except for the class rename (`copilot-table*` → `md-table*`).
- Raw HTML embedded in a wiki summary is escaped, never emitted as markup.
- Repository / RAGS / Foundation / Web unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- RTF/PDF/any export or download format for wiki pages.
- Markdig or full GFM support (links/images/fenced code blocks).
- Persisting a per-user default tab or wiring the tab through the API/DTOs.
- Editing surface changes (the editor textarea stays raw markdown).
