# Backlog: Normalized Lexicon for Term Resolution

**Status:** **Proposed** — not yet promoted to a sprint. No work authorized.
**Created:** 2026-08-14
**Source:** Project-owner review of a retrieval failure. Copilot missed the RFP due dates even though they were on the first page of the source ("Proposal Due Date: February 24, 2022, at 2:00 p.m. EST"). A second source phrased the same concept differently ("Bid due: August 26, 2026, 2:00 PM Pacific Time") and was missed too. Diagnosis: this is **not a bug** — it is the systematic limit of retrieval. Vector similarity + the whole-string ILIKE keyword fallback both fail on terse, varied-phrase facts. The fix generalizes the Sprint 69 "ground truth" instinct: instead of a binary ingested/not-ingested signal, give the platform a **canonical lexicon** that resolves terminology diversity across source documents.

## Problem

Documents express the same concept in many surface forms ("Bid due", "Proposal Due Date", "deadline", "closing date", "no later than", "submission deadline"). Today:

- Retrieval is **statistical, not semantic**: top-K embedding similarity + a literal keyword fallback. A fact phrased differently from the query is a miss, no matter how prominent it is in the document.
- There is **no canonical vocabulary**: no single place that says "these surface forms all mean `due_date`", so every new document that imposes new terminology is a new miss.
- Facts (dates, budgets, page limits, vendor names) are **not structured**: they live only inside chunks, so they cannot be queried deterministically or surfaced in the UI.

## Decisions made (2026-08-14)

1. **A lexicon is a concept registry, not a word list.** Each entry is a canonical concept (`due_date`, `budget`, `page_limit`, `vendor`, `submission`, …) with a label, an **alias set** of surface forms, an optional **value pattern** (date, currency, page count, free text), and optional **per-template scoping** (an RFP template's `due_date` vs a contract's `effective_date`). This is a controlled vocabulary, not a bag of synonyms.
2. **Two-sided application.** The lexicon is applied at **ingestion** (recognize aliases in extracted text → normalize to the canonical concept → store as structured facts) **and** at **query time** (expand the user's query concepts into their alias set before embedding + keyword fallback). One side without the other leaves the gap half-closed.
3. **Build on existing machinery, don't invent new plumbing.** `QueryExpander` (Sprint 68) is the query-time expansion hook — extend it from acronyms to concept expansion. `ITermNormalizer`/`ConfigurableTermNormalizer` (RAGS.Application) already normalize terms for taxonomy/ontology. The Sprint 61 settings foundation (`app_settings` + `ISettingsService` + admin `/settings` panel) is the natural home for admin-editable lexicon entries. The template `Uncategorized` → `POST /api/knowledge/reevaluate` flow is the precedent for the governance loop.
4. **Structured facts are the durable output.** Ingestion-time normalization writes rows (source_id, concept, value, page/offset) so a fact is deterministic and queryable — the same "durable ground truth" principle as Sprint 69's embeddings-based status. Facts can later surface in Browse, Copilot context, and the document viewer.
5. **Governance loop mirrors the template flow.** Surface forms in new documents that match no alias are reported as **unmapped terms**; an admin reviews and adds aliases; the lexicon grows. New source documents impose new vocabularies and the platform absorbs them instead of missing them.
6. **Lexicon entries are data, not code.** Seeded from config/`docs/doc-templates`-adjacent defaults, overridable at runtime via the settings panel — same config-seed/settings-override precedence as the Sprint 61 settings foundation.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Lexicon data model + repository** — canonical concept registry (concept key, label, alias set, value pattern, template scope) persisted in PostgreSQL; `ILexiconRepository` + `PostgreSqlLexiconRepository`; seeded defaults for the first concepts (due date, budget, page limit, vendor, submission). | The registry is the whole feature; without persistence the lexicon is a hard-coded dictionary. | ~1 day | Proposed |
| 2 | **Ingestion-time fact extraction** — during extraction/chunking, scan text for alias matches (with value-pattern capture), normalize to canonical concepts, write `document_facts` rows (source_id, concept, value, page/offset). Reuses the page-aware extraction from Sprint 67. | Turns terse facts into deterministic, queryable data — the core of the fix. | ~1.5–2 days | Proposed |
| 3 | **Query-time concept expansion** — extend `QueryExpander` (or a sibling `LexiconExpander`) to expand query concepts into their alias set before embedding; keyword fallback keeps the original query (Sprint 68 behavior). | Closes the retrieval gap on the query side; "when is it due" retrieves documents that say "Bid due". | ~0.5–1 day | Proposed |
| 4 | **Unmapped-term governance** — surface forms that match no alias are collected per source; `GET /api/lexicon/unmapped` (admin) lists them; admin adds aliases via the settings panel; re-extraction picks them up. | The growth loop — new documents' vocabularies get absorbed, not missed. | ~1 day | Proposed |
| 5 | **Admin settings panel card** — "Normalized Lexicon" card on `/settings` (behind the existing admin gate): browse concepts + aliases, add/remove aliases, review unmapped terms. | Gives operators the governance surface; the lexicon is only as good as its maintenance. | ~0.5–1 day | Proposed |
| 6 | **Surfacing** — Browse/Copilot can show normalized facts (e.g., a "Key facts" line per document); document viewer links facts to their page/offset. | Makes the structured output visible and verifiable (Sprint 67 viewer precedent). | ~0.5–1 day | Proposed |
| 7 | **Tests** — lexicon repository tests, fact-extraction tests (alias + value-pattern capture, page anchoring), query-expansion tests, governance/controller auth tests, Web binding tests. | The normalization rules are the point; they must be locked down. | ~0.5–1 day | Proposed |

## Suggested Sequencing

- **Items 1 + 2 first** — the registry and ingestion-time extraction are the foundation; they alone fix the due-date class of failures.
- **Item 3 next** — query-side expansion closes the loop so existing queries benefit without re-ingestion.
- **Items 4 + 5 together** — the governance loop and the admin surface are two halves of the same maintenance story.
- **Item 6** — surfacing after facts exist; **Item 7** alongside each item, not a trailing batch.

**Total (agent):** ~5–7 working days including build/test verification. Realistically a multi-sprint feature; the first sprint should scope to Items 1–3 (the retrieval fix) with 4–6 as a follow-up.

## Out of Scope

- LLM-based free-form fact extraction (this is deterministic alias+pattern matching; LLM extraction is a later, separate enhancement).
- Per-user lexicons (global/app-level only).
- Machine-translation or cross-language normalization (English-first; the alias set is the mechanism for any language later).
- Replacing the taxonomy/ontology entity machinery — the lexicon is complementary (facts vs. entities).
