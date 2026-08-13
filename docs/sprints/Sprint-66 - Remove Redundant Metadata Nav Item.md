# Sprint 66 - Remove Redundant Metadata Nav Item

**Status:** Active (2026-08-13)

Full authority: this file. Sprint 65 (Wiki Markdown and HTML View Tabs) is **complete, committed, and pushed** (`121cfd6` on `origin/master`).

Promotes `docs/backlog/Remove-Redundant-Metadata-Nav-Item.md` — the project owner's observation that the **Metadata** side-menu item duplicates **Browse**: both list files and both lead to the metadata editor, and Browse's ✎ Edit action already deep-links to `metadata?fileId=...`.

## Objective

Remove the **Metadata** entry from `NavMenu.razor` so the nav surfaces only the primary flows (Dashboard, Browse, Upload, Search, Wiki, Graph, Copilot, Governance, Settings). The `/metadata` page, its route, and Browse's Edit deep-link stay untouched — the editor remains reachable through the flow that owns it.

## Background

- `NavMenu.razor` (lines 49–54) has a `NavLink href="metadata"` block.
- `MetadataEditor.razor` (`/metadata`) is a file-picker + editor; its own info alert says "Select a file below (or use the ✎ Edit action in Browse)" — it points users to Browse.
- `Browse.razor` row action: `<a href="metadata?fileId=...&fileName=...&version=...">` (the ✎ Edit button).
- The "Searching…" hang the owner saw on the Metadata/Browse search is a **separate diagnostic** — `GET /api/search` → `SearchUseCase` → plain PostgreSQL metadata query (no LLM), so a long "Searching…" means the API is not responding, not that search is slow. Out of scope.

## Deliverables

### 1. Remove the Metadata nav item
- Delete the `NavLink href="metadata"` block from `src/Aletheia.Web/Layout/NavMenu.razor`. No page, route, or API changes; Browse's Edit deep-link and `Download.razor`'s "Open Browse" link keep working.

### 2. Binding test
- `tests/Aletheia.Web.UnitTests/`: assert `NavMenu.razor` no longer contains `href="metadata"` and that `Browse.razor` still contains `metadata?fileId=` (the Edit action preserved).

### 3. Docs
- `docs/File 02-Current-Sprint.md` (this becomes the active sprint), AGENTS.md, CLAUDE.md, `docs/File 03-openhands.md`, this sprint file's Implementation Status, and the backlog item (moved to `docs/backlog/archive/` with its status header updated when complete).

## Acceptance Criteria

- The Metadata entry is gone from the side nav; `/metadata` still resolves and Browse's ✎ Edit action still opens the editor.
- Web unit suite green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Removing/renaming the `/metadata` route or `MetadataEditor.razor`.
- Changing Browse's Edit action or the metadata editor.
- The "Searching…" hang diagnostic (API availability).
