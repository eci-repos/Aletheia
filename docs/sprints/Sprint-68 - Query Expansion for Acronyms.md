# Sprint 68 - Query Expansion for Acronyms

**Status:** Active (2026-08-13)

Full authority: this file. Sprint 67 (Source Verification: View the Exact Passage in the Document) is **complete, committed, and pushed** on `origin/master` (`9cbd886`).

Promotes `docs/backlog/Query-Expansion-for-Acronyms.md` — a user-reported retrieval miss. A broad Copilot question ("provide a summary of RFP opportunities related to AI") missed a fully-ingested AI RFP whose content is phrased as "Generative AI"/"GenAI" disclosure clauses, while a specific follow-up ("list CMP 2026 AI required features") found it immediately. The two-letter acronym "AI" does not reliably connect to "Artificial Intelligence" in the local embedding model.

## Objective

Expand domain acronyms in the user query **before embedding** so a short acronym ("AI") retrieves documents that spell it out ("Artificial Intelligence", "Generative AI"), while keeping the literal acronym for the keyword fallback. Deterministic, cheap, no infra change.

## Decisions (from the backlog item, settled 2026-08-13)

1. **Expand before embedding, keep the original token.** "AI features" → "AI Artificial Intelligence features" for embedding; the literal acronym still matches.
2. **Single-pass, word-boundary aware, case-insensitive.** "GenAI" wins over "AI" at the same position; "email"/"AIM" untouched; expansion text never re-scanned.
3. **Keyword fallback keeps the original query** — `PgVectorStore.SearchKeywordAsync` is a whole-string `ILIKE '%query%'` match; the expanded phrase would match nothing.
4. **Static domain dictionary, extensible** — `QueryExpander.Expansions` in `RAGS.Application`; no config plumbing in v1.

## Deliverables

### 1. QueryExpander
- `src/RAGS.Application/QueryExpander.cs`: static class with a public `Expansions` dictionary (AI, GenAI, RFP, RFI, ML, LLM, NLP, API, SOW, SLA, KPI, POC, MVP, OCR, PDF, SQL, RAG) and `Expand(string)` — a single-pass, longest-first, word-boundary regex that appends the expansion after each standalone acronym.
- `RagsService.RetrieveAsync` expands the query for the embedding call only; the keyword fallback keeps `request.Query`.

### 2. Tests
- `QueryExpanderTests` (7): expansion keeps the original token, case-insensitivity, no in-word expansion, multi-acronym, longest-first ("GenAI" over "AI", no cascade), null/whitespace, no-op for acronym-free queries.
- `RagsServiceTests` (+2): the expanded query reaches the embedding provider; the keyword fallback searches the original query.

### 3. Docs
- AGENTS, CLAUDE, File 02/03, this sprint file; backlog item archived.

## Acceptance Criteria

- `QueryExpander.Expand("AI features")` yields "AI Artificial Intelligence features"; "email"/"AIM" are untouched.
- `RagsService.RetrieveAsync` embeds the expanded query and keyword-falls-back on the original.
- RAGS unit suite green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Config-driven expansion dictionaries (static dictionary in v1).
- Expansion for the keyword fallback path (would break the ILIKE match).
- Applying expansion to GraphRAG/LazyGraphRAG direct vector-store calls (internal operator modes).
- A stronger embedding model (separate, infra-level option).

---

## Implementation Status

**Implemented, committed, and pushed (2026-08-13).** All 3 items complete.

### Item 1 — QueryExpander
- `src/RAGS.Application/QueryExpander.cs`: static `QueryExpander` with a public `Expansions` dictionary (17 domain acronyms: AI, GenAI, RFP, RFI, ML, LLM, NLP, API, SOW, SLA, KPI, POC, MVP, OCR, PDF, SQL, RAG) and `Expand(string)` — a single-pass, longest-first, word-boundary regex (`\b(GenAI|AI|…)\b`, IgnoreCase, Compiled) that appends the expansion after each standalone acronym. The original token is always kept; an expansion's own text is never re-scanned.
- `RagsService.RetrieveAsync` expands the query for the embedding call only (`QueryExpander.Expand(request.Query)`); the keyword fallback keeps `request.Query` because `PgVectorStore.SearchKeywordAsync` is a whole-string `ILIKE '%query%'` match. The expanded query is logged for diagnostics.

### Item 2 — Tests
- **RAGS 302** (+9): `QueryExpanderTests` (7 — expansion keeps the original token, case-insensitivity, no in-word expansion, multi-acronym, longest-first "GenAI" over "AI" with no cascade, null/whitespace, no-op for acronym-free queries) + `RagsServiceTests` (+2 — `RetrieveAsync_embeds_expanded_query_for_acronyms` via a `RecordingEmbeddingProvider`, `RetrieveAsync_keyword_fallback_uses_original_query` via `FakeVectorStore.LastKeywordQuery`).
- Foundation 55 / Repository 134 / Web 76 unchanged; `dotnet build Aletheia.slnx` succeeds (0 errors).

### Item 3 — Docs
- This sprint file, File 02/03, AGENTS.md, CLAUDE.md updated; backlog item archived.

**Residual manual (user-side):** hard-refresh `/search` and `/copilot`, then re-ask the broad question ("provide a summary of RFP opportunities related to AI") to confirm the AI RFP is now retrieved.
