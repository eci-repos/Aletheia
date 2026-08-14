# Backlog: Lexicon Governance and Glossary Surface

**Status:** **Proposed** — not yet promoted to a sprint. No work authorized.
**Created:** 2026-08-14
**Source:** Project-owner direction following Sprint 70 (Normalized Lexicon / Grounded Semantic Extraction). Sprint 70 built the data-collection loop — `document_facts` rows are queryable and unmapped concept hints accumulate in `lexicon_unmapped_terms` — but explicitly deferred the governance *surface* and fact *surfacing*. This item closes the loop: a glossary/lexicon for a given document domain that **end users can view and download** and **admins can extend and manage**.

## Problem

- **The lexicon is invisible.** Concepts and aliases live in `LexiconSeedData` + the SQL seed — editable only in code. There is no way to browse what the platform knows, add an alias, or correct a concept without a deploy.
- **The growth loop is half-closed.** Unmapped terms accumulate in `lexicon_unmapped_terms` with no review surface, so new documents' vocabularies are collected but never absorbed.
- **Facts are structured but not surfaced.** `document_facts` rows (concept, normalized value, source span, page/offset) are deterministic and queryable, but nothing in the UI shows them.
- **`template_scope` is dormant.** The column exists on `LexiconConcept` but matching is global — a "glossary for a given document domain" needs domain scoping.
- **No export.** The glossary cannot be shared or downloaded for offline use.

## Decisions made (2026-08-14)

1. **Two surfaces, one sprint.** (a) An **admin management surface** — browse concepts + aliases, add/remove aliases, add concepts, edit value patterns, review unmapped terms. (b) An **end-user read-only glossary** — per-domain concept list with the verified facts, downloadable. The admin surface is the growth mechanism; the end-user surface is the surfacing.
2. **Admin surface follows the Sprint 61 settings-panel pattern.** Admin-only API (`GET/PUT /api/lexicon/concepts`, `GET /api/lexicon/unmapped`, `POST /api/lexicon/unmapped/{id}/resolve`) + an admin-gated UI (a dedicated `/lexicon` page or a card on `/settings`). The API enforces the Administrator role; the UI hides the surface for non-admins.
3. **End-user glossary is read-only and domain-scoped.** A glossary for a given document domain (template) lists that domain's concepts, aliases, and per-document verified facts. **`template_scope` becomes enforced** — a concept with a template scope applies only to documents of that template; unscoped concepts stay global. This is the connective tissue that makes the glossary per-domain rather than a flat list.
4. **Admin edits are data, not code.** Alias/concept edits persist to the lexicon tables and feed re-extraction; they **never bypass the fidelity gate** — a fact is still stored only when its span exists and its value parses. `LexiconSeedData` + the SQL seed remain the defaults; admin edits override at runtime (same config-seed/settings-override precedence as the Sprint 61 settings foundation). The `LexiconSeedData` ↔ SQL-seed mirror (`LexiconBindingTests`) is untouched.
5. **Unmapped-term review is the growth mechanism.** An admin confirms a hint → it becomes an alias on an existing concept (or a new concept) → re-extraction picks it up. Dismissed hints are marked reviewed, not deleted.
6. **Download is a first-class deliverable.** CSV + JSON export of the glossary (concepts + aliases) and optionally the per-document facts.

## Candidate Work Items

| # | Item | Why it matters | Effort (agent) | Status |
|---|------|----------------|----------------|--------|
| 1 | **Admin management surface** — browse concepts + aliases, add/remove aliases, add concepts, edit value patterns; review unmapped terms (confirm → alias/new concept, dismiss). API `GET/PUT /api/lexicon/concepts`, `GET /api/lexicon/unmapped`, `POST /api/lexicon/unmapped/{id}/resolve` (Administrator). | The growth mechanism — without it the lexicon is a hard-coded dictionary. | ~1.5–2 days | Proposed |
| 2 | **`template_scope` enforcement** — concept matching becomes domain-scoped: a concept with a template scope applies only to documents of that template; unscoped concepts stay global. | Makes the glossary per-domain; the column already exists. | ~0.5 day | Proposed |
| 3 | **End-user glossary view** — read-only per-domain glossary (concept, label, aliases, per-document verified facts), reachable from Browse/document viewer and/or a dedicated page. | Surfacing the structured output — the "make facts visible" follow-up. | ~1 day | Proposed |
| 4 | **Download/export** — CSV + JSON export of the glossary (and optionally the facts). | The glossary becomes shareable/offline-usable. | ~0.5 day | Proposed |
| 5 | **Tests + docs** — controller auth tests, repository round-trips, glossary scoping tests, Web binding tests; AGENTS/CLAUDE/File 02/03 + sprint file; backlog item archived. | The governance surface must be locked down. | ~0.5–1 day | Proposed |

## Suggested Sequencing

- **Items 1 + 2 together** — the admin surface and domain scoping are two halves of the same management story; scoping changes matching, so it lands with the surface that edits concepts.
- **Items 3 + 4 together** — the end-user view and its export are one deliverable.
- **Item 5** alongside each item, not a trailing batch.

**Total (agent):** ~4–5 working days including build/test verification. A single sprint.

## Out of Scope

- Changing the fidelity gate or the propose → verify → normalize → persist pipeline (Sprint 70) — admin edits are data that feed re-extraction, never a bypass.
- Per-user lexicons (global/app-level + per-domain only).
- Machine translation or cross-language normalization (English-first; the alias set is the mechanism for any language later).
- Replacing the taxonomy/ontology entity machinery — the lexicon is complementary (facts vs. entities).
- Editing `LexiconSeedData`/SQL-seed defaults from the UI (admin edits override at runtime; the seed stays the code-owned default).
