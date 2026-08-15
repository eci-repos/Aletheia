# Backlog: Search UX Clarity — Semantic vs Summaries

**Status:** Implemented — all 5 items delivered (Sprint 72, 2026-08-15)
**Created:** 2026-08-15
**Source:** Product/UX review (2026-08-15) — three clarity gaps in the search surfaces: (1) the Browse "Search files..." box does not say what it searches; (2) the GraphRAG/LazyGraphRAG summary search is invisible to end users (mode buttons gated behind `FeatureFlags:ShowInternalSearch`, default false); (3) when summaries get created and how they are managed is opaque, which matters on large KBs where summaries are load-bearing.

## Problem

- **Browse search is ambiguous.** `Browse.razor`'s search box (placeholder "Search files...") calls `GET /api/search` — a plain PostgreSQL query over file *metadata* (name, tags, content hash), not document content. Users cannot tell it is a metadata filter, and may expect content search.
- **Summary search is invisible.** Search Center's mode buttons (WRAGS / GraphRAG / LazyGraphRAG) only render when `FeatureFlags:ShowInternalSearch` is true (default **false**). A normal user has no way to choose "search the summaries" at all.
- **Jargon leaks to users.** "GraphRAG" and "LazyGraphRAG" are production details (batch-built at ingest vs. built on demand at query time). Product-wise they are the same thing — a summaries search over a large KB — and surfacing both names clutters the user's mental model.
- **Summary creation/management is opaque.** There is no way to know whether a document has summaries, when they were built, or how to rebuild/re-cluster them. On large KBs where summaries play a required role, that is a blind spot.

## Decisions (proposed approach)

1. **Two user-facing search modes, gentle naming.** Search Center exposes **Semantic** (exact passages) and **Summaries** (higher-level synthesized answers). "Graph" / "LazyGraph" never appear in user-facing copy. The backend resolves the Summaries mode: prefer pre-built GraphRAG community summaries when they exist, fall back to LazyGraphRAG query-time traversal when they don't. WRAGS/LazyGraphRAG remain internal operator modes behind `ShowInternalSearch`.
2. **Info icon on both modes.** A hover/click info icon explains each mode for the curious — e.g. *"Summaries are generated from the connections between your documents. On large knowledge bases they may take time to appear."* — without cluttering the default view.
3. **Browse search states what it does.** A short caption under the box — e.g. *"Search file metadata (name, tags, content hash) — not document content."* — plus an info icon explaining the fields it matches and pointing to Search Center for content search.
4. **De-murkify summary creation/management.** One user-facing story ("summaries exist once the graph has been built; they may take time on large KBs") plus an operator/admin path: per-document summary status (mirroring the Sprint 69 Ingestion column), and admin actions to trigger/regenerate summaries and re-cluster. Operator vocabulary stays admin-side.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Browse search caption + info icon** — label/short sentence under the search box stating it is a metadata filter, with an info tooltip covering the fields it matches and a pointer to Search Center for content search. | Users know what the box does before typing; no more "is this content search?" confusion. | ~0.5 day | Proposed |
| 2 | **Search Center "Semantic / Summaries" modes** — always-visible user-facing toggle; Summaries resolves to GraphRAG-first, LazyGraphRAG-fallback in the backend; WRAGS/LazyGraphRAG stay behind `ShowInternalSearch`. | End users can actually choose summary search; jargon stays out of the UI. | ~1 day | Proposed |
| 3 | **Mode info icons** — hover/click explanations for Semantic and Summaries (what they return, when summaries exist). | The curious get detail; the default view stays clean. | ~0.5 day | Proposed |
| 4 | **Summary status + admin management** — per-document summary state surfaced (like the Ingestion column), plus admin actions to trigger/regenerate summaries and re-cluster; single user-facing "when do summaries exist" story. | Large-KB operators can see and control summary coverage instead of guessing. | ~1.5–2 days | Proposed |
| 5 | **Tests + docs** — Web unit tests for the new mode toggle/captions; AGENTS/CLAUDE/File 02/03 + sprint file; backlog item archived. | The UX contract is locked down. | ~0.5–1 day | Proposed |

## Suggested Sequencing

- **Items 1 + 2 + 3 together** — the user-facing clarity work is one coherent change: Browse says what it does, Search Center offers Semantic/Summaries with gentle naming and info icons.
- **Item 4** — summary status + admin management is the larger piece; it can land as its own follow-up (or a second sprint) once the user-facing modes are in.
- **Item 5** alongside each item, not a trailing batch.

**Total (agent):** ~3.5–5 working days including build/test verification. Items 1–3 fit one sprint; item 4 may warrant its own.

## Out of Scope

- A new dedicated surface for browsing summaries (the Search Center mode toggle is the surface).
- Renaming the internal GraphRAG/LazyGraphRAG services, controllers, or API routes (internal code/docs may keep the terms; only user-facing copy changes).
- Changing how summaries are produced (GraphRAG ingest-time vs LazyGraphRAG query-time behavior is untouched).
- Making summary generation distributed or multi-host.
