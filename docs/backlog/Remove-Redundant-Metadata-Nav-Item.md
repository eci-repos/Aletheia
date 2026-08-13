# Backlog: Remove Redundant Metadata Nav Item

**Status:** **Proposed** — not yet promoted to a sprint. No work authorized.
**Created:** 2026-08-13
**Source:** Design review with the project owner. The **Metadata** side-menu item (`NavMenu.razor` → `/metadata`, `MetadataEditor.razor`) is a redundant entry point: the page is a file-picker that opens the metadata editor, and **Browse** already provides the same flow via its ✎ Edit action, which deep-links to `metadata?fileId=...&fileName=...&version=...`. The standalone nav item adds a second, weaker path to the same editor with no unique value.

## Problem

- The Metadata nav item duplicates Browse: both list files and both lead to the metadata editor.
- A user landing on `/metadata` sees an info alert pointing them to Browse ("Select a file below (or use the ✎ Edit action in Browse)") — the page itself acknowledges Browse is the primary surface.
- A cluttered nav makes the primary surfaces (Dashboard, Browse, Upload, Search, Wiki) harder to find.

## Decisions made (2026-08-13)

1. **Remove the Metadata entry from `NavMenu.razor` only.** The `/metadata` page and its route stay — Browse's ✎ Edit action, `Download.razor`'s "Open Browse" link, and any deep links keep working. The page is reachable through the flow that owns it.
2. **No API, route, or page changes.** This is a pure navigation-surface change; `MetadataEditor.razor` is untouched.
3. **The "Searching…" hang the owner saw on the Metadata/Browse search is a separate diagnostic** (the search is a plain PostgreSQL metadata query via `GET /api/search` → `SearchUseCase` — no LLM — so a long "Searching…" means the API is not responding, not that search is slow). Out of scope here.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Remove the Metadata nav item** — delete the `NavLink href="metadata"` block from `src/Aletheia.Web/Layout/NavMenu.razor`; leave the page, route, and Browse Edit deep-link intact. | Declutters the nav; removes a redundant path to the metadata editor. | ~0.1 day | Proposed |
| 2 | **Binding test** — assert `NavMenu.razor` no longer contains `href="metadata"` and that `Browse.razor` still links to `metadata?fileId=` (the Edit action). | Locks the intent: nav entry gone, deep-link preserved. | ~0.1 day | Proposed |
| 3 | **Docs** — Architecture/user-guide nav description, AGENTS, File 02/03, sprint file when promoted. | Documentation mandate. | ~0.1 day | Proposed |

## Suggested Sequencing

- **Item 1 first**, then **item 2** (test locks the change), **item 3** alongside.

**Total (agent):** ~0.25 working day including build/test verification. No API, schema, or wire changes.

## Out of Scope

- Removing or renaming the `/metadata` route or `MetadataEditor.razor` page.
- Changing Browse's Edit action or the metadata editor itself.
- The "Searching…" hang diagnostic (API availability, not code).
