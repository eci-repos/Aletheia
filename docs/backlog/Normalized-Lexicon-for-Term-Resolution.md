# Backlog: Normalized Lexicon for Term Resolution (Grounded Semantic Extraction)

**Status:** **Proposed** — not yet promoted to a sprint. No work authorized.
**Created:** 2026-08-14
**Source:** Project-owner review of a retrieval failure. Copilot missed the RFP due dates even though they were on the first page of the source ("Proposal Due Date: February 24, 2022, at 2:00 p.m. EST"). A second source phrased the same concept differently ("Bid due: August 26, 2026, 2:00 PM Pacific Time") and was missed too. Diagnosis: this is **not a bug** — it is the systematic limit of retrieval. Vector similarity + the whole-string ILIKE keyword fallback both fail on terse, varied-phrase facts. The fix generalizes the Sprint 69 "ground truth" instinct: give the platform a **canonical lexicon** that resolves terminology diversity across source documents — and, per project-owner direction (2026-08-14), make it **semantic** (understands paraphrase and novel terminology) **without losing fidelity to the source** (nothing stored that isn't verifiable in the text).

## Problem

Documents express the same concept in many surface forms ("Bid due", "Proposal Due Date", "deadline", "closing date", "no later than", "submission deadline"). Today:

- Retrieval is **statistical, not semantic**: top-K embedding similarity + a literal keyword fallback. A fact phrased differently from the query is a miss, no matter how prominent it is in the document.
- There is **no canonical vocabulary**: no single place that says "these surface forms all mean `due_date`", so every new document that imposes new terminology is a new miss.
- Facts (dates, budgets, page limits, vendor names) are **not structured**: they live only inside chunks, so they cannot be queried deterministically or surfaced in the UI.
- A pure alias-matching lexicon would fix only *known* concepts — a bounded dictionary, not semantic adaptation. A pure LLM extraction would understand anything but risks hallucination (storing facts not in the source). **The design must get both: semantic coverage and source fidelity.**

## Decisions made (2026-08-14)

1. **Grounded semantic extraction is the core — propose → verify → normalize.** The LLM is the *recognition* layer (semantic, wide coverage); the source text is the *fidelity* gate; the lexicon is the *normalization* layer (canonical structure). No single layer carries the whole burden.
2. **Propose.** At ingestion, an LLM pass over each chunk/page proposes candidate facts: `{concept_hint, value, source_span, page, offset}`. The LLM is instructed to quote the **exact source span** it read the value from. This is the semantic adaptation — paraphrase and novel terminology are understood without a curated alias.
3. **Verify (the fidelity gate — the crux).** Each proposal's `source_span` must actually exist in the extracted text (whitespace-normalized string match), and the `value` must be a faithful parse of that span (date parser confirms "February 24, 2022"; currency parser confirms "$1.2M"). Proposals that fail are **dropped or flagged, never stored**. Nothing enters the knowledge base that isn't in the source. Reuses the Sprint 67 span/offset machinery (page-aware extraction, `Chunk.PageNumber`/`OffsetInPage`, the `/document/{id}` viewer).
4. **Normalize.** Verified facts map to canonical concepts via the lexicon (concept key, label, alias set, value pattern, optional template scope). Novel surface forms become **candidate aliases** — the governance loop becomes semi-automatic (LLM proposes, admin confirms), mirroring the template `Uncategorized` → `POST /api/knowledge/reevaluate` flow.
5. **Structured facts are the durable output.** Ingestion-time normalization writes rows (source_id, concept, value, page/offset) so a fact is deterministic and queryable — the same "durable ground truth" principle as Sprint 69's embeddings-based status. Facts surface in Browse, Copilot context, and the document viewer.
6. **Two-sided application.** The lexicon is applied at **ingestion** (extraction + normalization) **and** at **query time** (expand the user's query concepts into their alias set before embedding + keyword fallback — extending `QueryExpander` from Sprint 68). One side without the other leaves the gap half-closed.
7. **Build on existing machinery.** `QueryExpander` (Sprint 68) is the query-time hook; `ITermNormalizer`/`ConfigurableTermNormalizer` (RAGS.Application) already normalize terms; `EntityExtractionService` + `NoiseEntityFilter` prove the LLM-extraction-with-filtering pattern; the Sprint 61 settings foundation (`app_settings` + `ISettingsService` + admin `/settings` panel) is the home for admin-editable lexicon entries.
8. **Lexicon entries are data, not code.** Seeded from config defaults, overridable at runtime via the settings panel — same config-seed/settings-override precedence as the Sprint 61 settings foundation.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Lexicon data model + repository** — canonical concept registry (concept key, label, alias set, value pattern, template scope) persisted in PostgreSQL; `ILexiconRepository` + `PostgreSqlLexiconRepository`; seeded defaults for the first concepts (due date, budget, page limit, vendor, submission). | The registry is the normalization layer; without persistence the lexicon is a hard-coded dictionary. | ~1 day | Proposed |
| 2 | **Grounded fact extraction (propose + verify)** — LLM pass at ingestion proposing `{concept_hint, value, source_span, page, offset}`; a **verification gate** checks span existence + value parse before anything is stored; verified facts write `document_facts` rows. Reuses Sprint 67 page-aware extraction. | The core of the feature: semantic coverage with a fidelity guarantee. | ~2–2.5 days | Proposed |
| 3 | **Query-time concept expansion** — extend `QueryExpander` (or a sibling `LexiconExpander`) to expand query concepts into their alias set before embedding; keyword fallback keeps the original query (Sprint 68 behavior). | Closes the retrieval gap on the query side; "when is it due" retrieves documents that say "Bid due". | ~0.5–1 day | Proposed |
| 4 | **Candidate-alias governance** — novel surface forms from verified facts become candidate aliases; `GET /api/lexicon/unmapped` (admin) lists them; admin confirms/edits via the settings panel; re-extraction picks them up. | The growth loop — new documents' vocabularies get absorbed, not missed. | ~1 day | Proposed |
| 5 | **Admin settings panel card** — "Normalized Lexicon" card on `/settings` (behind the existing admin gate): browse concepts + aliases, add/remove aliases, review candidate aliases and verification-flagged facts. | Gives operators the governance surface; the lexicon is only as good as its maintenance. | ~0.5–1 day | Proposed |
| 6 | **Surfacing** — Browse/Copilot show normalized facts (e.g., a "Key facts" line per document); document viewer links facts to their page/offset (Sprint 67 precedent). | Makes the structured output visible and verifiable. | ~0.5–1 day | Proposed |
| 7 | **Tests** — lexicon repository tests; extraction tests (propose → verify: span match, value parse, drop-on-failure, page anchoring); query-expansion tests; governance/controller auth tests; Web binding tests. | The propose→verify→normalize pipeline is the point; it must be locked down. | ~0.5–1 day | Proposed |

## Suggested Sequencing

- **Items 1 + 2 first** — the registry and the grounded extraction pipeline are the foundation; they alone fix the due-date class of failures with fidelity.
- **Item 3 next** — query-side expansion closes the loop so existing queries benefit without re-ingestion.
- **Items 4 + 5 together** — the governance loop and the admin surface are two halves of the same maintenance story.
- **Item 6** — surfacing after facts exist; **Item 7** alongside each item, not a trailing batch.

**Total (agent):** ~6–8 working days including build/test verification. Realistically a multi-sprint feature; the first sprint should scope to Items 1–3 (the retrieval fix) with 4–6 as a follow-up.

## Out of Scope

- Per-user lexicons (global/app-level only).
- Machine-translation or cross-language normalization (English-first; the alias set is the mechanism for any language later).
- Replacing the taxonomy/ontology entity machinery — the lexicon is complementary (facts vs. entities).
- Unverified LLM extraction: the verify gate is mandatory, not optional — a proposal that cannot be anchored to a real source span is never stored.
