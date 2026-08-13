# Backlog: Query Expansion for Acronyms

**Status:** **Implemented (Sprint 68, 2026-08-13) — archived.** All 3 items delivered; see `docs/sprints/Sprint-68 - Query Expansion for Acronyms.md` "Implementation Status". This file is the design record; the sprint file is the implementation authority.
**Created:** 2026-08-13
**Source:** User-reported retrieval miss. A user asked Copilot "provide a summary of RFP opportunities related to AI" and the AI RFP (CDF 2026 – 3. RFP Analysis) was not retrieved, even though it was fully ingested and a specific follow-up ("list CMP 2026 AI required features") found it immediately. The document's AI content is phrased as "Generative AI"/"GenAI" disclosure clauses; the query's two-letter acronym "AI" does not reliably connect to "Artificial Intelligence" in the local embedding model, so the query vector drifts off the AI chunks.

## Problem

Short domain acronyms in user queries ("AI", "RFP", "GenAI") are embedded as-is. When a document spells the term out ("Artificial Intelligence", "Request for Proposal", "Generative AI") or uses a sibling acronym ("GenAI"), the embedding model may not bridge the gap — especially a local model. The result is a **retrieval miss on broad/acronym-heavy queries** even though the document is ingested and relevant. Keyword fallback does not help because it only fires below a score threshold and matches the literal acronym, not the spelled-out term.

## Decisions made (2026-08-13)

1. **Expand acronyms before embedding, keep the original token.** The query "AI features" becomes "AI Artificial Intelligence features" for embedding — the literal acronym still matches, and the spelled-out term now aligns the vector with documents that use it.
2. **Expansion is single-pass, word-boundary aware, case-insensitive.** "GenAI" wins over "AI" at the same position; "email"/"AIM" are never touched; an expansion's own text is never re-scanned.
3. **Keyword fallback keeps the original query.** `PgVectorStore.SearchKeywordAsync` is a whole-string `ILIKE '%query%'` match — the expanded phrase would match nothing. The literal acronym already matches "Generative AI" via ILIKE.
4. **Static domain dictionary, extensible.** `QueryExpander.Expansions` is a public static dictionary in `RAGS.Application`; new acronyms are added there as they appear in the corpus. No config plumbing in v1.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **`QueryExpander`** — static class in `RAGS.Application` with a domain acronym dictionary and a single-pass word-boundary regex `Expand(string)`; applied in `RagsService.RetrieveAsync` to the embedding query only. | The core fix: acronym queries now retrieve documents that spell the term out. | ~0.5 day | Proposed |
| 2 | **Tests** — `QueryExpanderTests` (expansion, case-insensitivity, no in-word expansion, multi-acronym, longest-first, null/empty, no-op) + `RagsServiceTests` (expanded query reaches the embedding provider; keyword fallback keeps the original query). | Locks down the expansion behavior and the embedding/keyword split. | ~0.5 day | Proposed |
| 3 | **Docs** — AGENTS, CLAUDE, File 02/03, sprint file; backlog archived. | Standing documentation mandate. | ~0.25 day | Proposed |

## Suggested Sequencing

- **Item 1 first** — the expander and its single call site.
- **Item 2 alongside** — tests with the implementation.
- **Item 3 last** — docs once behavior is locked.

**Total (agent):** ~1 working day including build/test verification.

## Out of Scope

- Config-driven expansion dictionaries (static dictionary in v1; extend `QueryExpander.Expansions`).
- Expansion for the keyword fallback path (would break the ILIKE match).
- Applying expansion to GraphRAG/LazyGraphRAG direct vector-store calls (they route through `RagsService` for semantic fallback; direct-store paths are internal operator modes).
- A stronger embedding model (a separate, infra-level option).
