# Backlog: Canonical Form, Themes, and Filtering Enhancements

**Status:** Items 1-4 promoted to Sprint 59 (2026-08-07) and implemented; item 5 still deferred.
**Created:** 2026-08-07
**Source:** Review of the canonical-template gate (`RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync`, `DocumentTemplateRegistry`), the theme model (`file_metadata.template_name`/`theme`, `ListSourceIdsByThemeAsync`, `GET /api/knowledge/themes`), and theme filtering (`ChatSession.ThemeFilter` -> `RetrievalRequest.SourceIds`). Companion discussion: `docs/LLM-Wiki-Semantic-Search-GraphRAG-LazyGraphRAG.md` (Section 2, 7).

Items here are **not** authorized work. An item becomes authorized only when the current sprint file promotes it. This document tracks candidate improvements so they are not lost; keep the Status column and this file current as work progresses.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Soften the canonical gate** — add `file_metadata.template_status` (`Canonical` / `Uncategorized` / `PendingTemplate`); ingest uncategorized documents (RAGS + knowledge index + graph seed) instead of hard-stopping; gate template-dependent features (document briefs, per-section retrieval, theme) on `Canonical`; admin list of uncategorized docs + re-evaluate/promotion trigger (reuse ingestion repair path) that generates the brief once a template matches. Keep content-quality gates (supported type, extractable text). | Estate completeness currently hinges on template coverage: a document with no matching template is refused entirely, so a new document kind arriving before its template is written is lost. | ~0.5–1 day | **Promoted -> Sprint 59, implemented** (2026-08-07). `PendingTemplate` folded into `Uncategorized`. |
| 2 | **Multi-theme per document** — templates declare a set of themes (`Theme: Analysis, As-Built`, backward compatible with a single value); `file_metadata.theme` becomes a set; `ListSourceIdsByThemeAsync` matches any selected theme; picker shows each theme with its document count (a doc in multiple themes counts in each). Filtering semantics unchanged (union for collections, intersection for named docs). | A single theme per document forces a choice; real documents are multi-faceted (an RFP can be both Analysis and As-Built relevant), making filtering coarse. | ~0.5 day | **Promoted -> Sprint 59, implemented** (2026-08-07). `TryGetThemes`, `theme text[]` + GIN, match-any via `ResolveSourceIdsAsync`. |
| 3 | **Persist derived themes (backfill)** — one-time job that derives and persists `template_name` + `theme` from the template registry (deterministic) for rows where they are null; demote the read-time file-name fallback to a safety net. | Pre-migration rows derive theme from the file name at read time — a heuristic that can drift from what ingestion would persist. | ~2–3 hrs | **Promoted -> Sprint 59, implemented** (2026-08-07) via `POST /api/knowledge/reevaluate`. |
| 4 | **Shared theme scope across surfaces (Phase 1)** — a shared scope state (localStorage) that Search Center honors as an optional theme filter, with a visible "scoped to themes" indicator; Copilot keeps its session-scoped filter; Wiki stays curated. | Theme filtering is currently Copilot-session-only, so the same document is theme-scoped in Copilot but not in Search Center/Wiki — inconsistent views of the estate. | ~1–1.5 days | **Promoted -> Sprint 59, implemented** (2026-08-07). Semantic search only; graph modes out of scope. |
| 5 | **Theme-aware graph retrieval (Phase 2, defer)** — extend theme enforcement to GraphRAG/LazyGraphRAG global/broad paths and community summaries. | Graph modes are currently explicitly out of scope for theme filtering (Sprint 58); a global knowledge-scope widget over graph surfaces is a natural follow-up. | ~1–2 days | Deferred |

## Suggested Sequencing

- **Core (items 1 + 2 + 3) in one pass** — they all touch the `file_metadata` schema and the theme model, so one migration covers all three. This is the coherent core (~1.5–2 days).
- **Item 4 separately** — largest UI/API surface; can follow the core.
- **Item 5 parked** — already on the future-state list; do not promote until 1–4 land.

**Total (agent):** ~3–4 working days including build/test verification and Docker smoke, excluding the deferred item 5.
