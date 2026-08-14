# Sprint 71 - Lexicon Governance and Glossary Surface

**Status:** Active (2026-08-14)

Full authority: this file. Sprint 70 (Normalized Lexicon / Grounded Semantic Extraction) is **complete, committed, and pushed** on `origin/master` (`229229d`).

Promotes `docs/backlog/Lexicon-Governance-and-Glossary-Surface.md` — the project-owner-directed follow-up to Sprint 70. Sprint 70 built the data-collection loop (`document_facts` rows are queryable; unmapped concept hints accumulate in `lexicon_unmapped_terms`) but explicitly deferred the governance *surface* and fact *surfacing*. This sprint closes the loop: a glossary/lexicon for a given document domain that **end users can view and download** and **admins can extend and manage**.

## Objective

Two surfaces, one sprint:

- **Admin management surface** — browse concepts + aliases, add/remove aliases, add concepts, edit value patterns, review unmapped terms (confirm → alias/new concept, dismiss). The growth mechanism: new documents' vocabularies get absorbed instead of missed.
- **End-user read-only glossary** — per-domain concept list with the verified facts, downloadable as CSV/JSON. The surfacing: structured facts become visible and verifiable.

The connective tissue is **`template_scope` enforcement**: a concept with a template scope applies only to documents of that template; unscoped concepts stay global. This is what makes the glossary per-domain rather than a flat list.

## Decisions (from the backlog item, settled 2026-08-14)

1. **Two surfaces, one sprint.** Admin management (growth mechanism) + end-user read-only glossary (surfacing).
2. **Admin surface follows the Sprint 61 settings-panel pattern.** Admin-only API + admin-gated UI (a dedicated `/lexicon` page). The API enforces the Administrator role; the UI hides the surface for non-admins.
3. **End-user glossary is read-only and domain-scoped.** `template_scope` becomes enforced at ingestion: a scoped concept only proposes/verifies facts for documents of its template; unscoped concepts stay global. Query-time expansion stays global (it is additive widening — a query has no template context).
4. **Admin edits are data, not code.** Alias/concept edits persist to the lexicon tables and feed re-extraction; they **never bypass the fidelity gate**. `LexiconSeedData` + the SQL seed remain the defaults; admin edits override at runtime. The `LexiconSeedData` ↔ SQL-seed mirror (`LexiconBindingTests`) is untouched.
5. **Unmapped-term review is the growth mechanism.** An admin confirms a hint → it becomes an alias on an existing concept (or a new concept) → re-extraction picks it up. Dismissed hints are marked resolved, not deleted.
6. **Download is a first-class deliverable.** CSV + JSON export of the glossary (concepts + aliases + facts).

## Deliverables

### 1. Admin lexicon management API + repository methods
- `ILexiconRepository` gains `DeleteConceptAsync` and `ResolveUnmappedTermAsync`; `GetUnmappedTermsAsync` returns **pending** terms only; `GetAllFactsAsync` for the glossary.
- `lexicon_unmapped_terms` gains `status` (`pending`/`resolved`) + `resolved_at` — idempotent migration `2026-08-14-lexicon-unmapped-status.sql` + `init.sql` + `PostgreSqlLexiconSchema`.
- `LexiconController` (Repository.API): `GET /api/lexicon/concepts` (authenticated read, optional `?template=`), `PUT /api/lexicon/concepts` (admin upsert), `DELETE /api/lexicon/concepts/{key}` (admin), `GET /api/lexicon/unmapped` (admin, pending), `POST /api/lexicon/unmapped/resolve` (admin). Admin writes invalidate the `LexiconProvider` cache.

### 2. `template_scope` enforcement in concept matching
- `FactVerifier.Verify` gains an optional `templateName` param — concepts whose `TemplateScope` is non-null and does not match the document's template are excluded from matching.
- `IFactExtractionService.ExtractAsync` / `GroundedFactExtractionService` gain the optional `templateName`; `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` passes the matched canonical template name.

### 3. End-user glossary view
- `GET /api/lexicon/glossary?template=` (authenticated) — concepts + facts (with source names via `IMetadataRepository`) for the glossary page.
- `Pages/Glossary/Index.razor` at `/glossary` — read-only per-domain glossary: concept, label, aliases, value pattern, and per-document verified facts (value, source, page). Domain filter + download buttons. Nav entry.

### 4. Download/export (CSV + JSON)
- `GET /api/lexicon/glossary/export?format=csv|json&template=` (authenticated) — file download of the glossary (concepts + aliases + facts).

### 5. Tests + docs
- **RAGS** (+): `FactVerifierTests` template-scope cases, `GroundedFactExtractionServiceTests` template-scope pass-through.
- **Repository** (+): `LexiconControllerTests` — auth (admin vs authenticated), upsert/delete, unmapped resolve, glossary + export.
- **Web** (+): `GlossaryBindingTests` + `LexiconBindingTests` additions — pages, nav entries, `RepositoryApiClient` methods, export wiring.
- AGENTS, CLAUDE, File 02/03, this sprint file; backlog item archived.

## Acceptance Criteria

- An admin can browse concepts + aliases, add/remove aliases, add/delete concepts, and review pending unmapped terms (confirm → alias, or dismiss) from `/lexicon`; edits take effect on the next read (cache invalidated) and never bypass the fidelity gate.
- A concept with a `template_scope` only produces facts for documents of that template; unscoped concepts stay global.
- An end user can view a per-domain glossary at `/glossary` (concept, aliases, verified facts with source + page) and download it as CSV or JSON.
- Repository + Web + RAGS + Foundation unit suites green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Changing the fidelity gate or the propose → verify → normalize → persist pipeline (Sprint 70).
- Per-user lexicons (global/app-level + per-domain only).
- Machine translation / cross-language normalization.
- Replacing the taxonomy/ontology entity machinery.
- Editing `LexiconSeedData`/SQL-seed defaults from the UI (admin edits override at runtime; the seed stays the code-owned default).

---

## Implementation Status

**Implemented (2026-08-14).** All 5 items complete; tests green.

### Item 1 — Admin lexicon management API + repository methods
- `ILexiconRepository` + `PostgreSqlLexiconRepository`: `DeleteConceptAsync`, `ResolveUnmappedTermAsync`, `GetAllFactsAsync`; `GetUnmappedTermsAsync` returns pending only.
- `lexicon_unmapped_terms` gains `status` + `resolved_at` (migration `2026-08-14-lexicon-unmapped-status.sql` + `init.sql` + `PostgreSqlLexiconSchema`).
- `LexiconController` (Repository.API): concepts read/upsert/delete, unmapped list/resolve; admin writes invalidate `LexiconProvider`.
- Admin UI: `Pages/Lexicon/Index.razor` at `/lexicon` (admin-gated via `AuthorizeView Roles="Administrator"`, nav entry in the Management group) — browse concepts + aliases, add/edit a concept (key, label, value pattern, template scope, comma-separated aliases), delete a concept, and dismiss pending unmapped terms.

### Item 2 — `template_scope` enforcement
- `FactVerifier.Verify` + `GroundedFactExtractionService.ExtractAsync` take an optional `templateName`; scoped concepts apply only to matching templates. `EnsureIngestedAsync` passes the canonical template name.

### Item 3 — End-user glossary view
- `GET /api/lexicon/glossary?template=` (concepts + facts with source names); `Pages/Glossary/Index.razor` at `/glossary` with domain filter + download; nav entry.

### Item 4 — Download/export
- `GET /api/lexicon/glossary/export?format=csv|json&template=` — CSV + JSON file download.

### Item 5 — Tests + docs
- **RAGS 343 (+5)**: `FactVerifierTests` template-scope cases (4 — scoped applies on match, out-of-scope → unmapped text, no-template → unmapped text, unscoped applies anywhere) + `GroundedFactExtractionServiceTests` template-scope pass-through (1).
- **Repository 151 (+13)**: `LexiconControllerTests` (13 — concepts read/filter, upsert/delete + provider invalidate, unmapped list/resolve, glossary source-name join + scope filter, CSV/JSON export).
- **Web 90 (+6)**: `LexiconBindingTests` (unmapped status columns in migration/init/schema, glossary page route + download buttons, glossary nav entry, admin `/lexicon` page + admin gate, admin nav entry, client methods).
- Foundation 55 unchanged; `dotnet build Aletheia.slnx` succeeds (0 errors). Docs updated; backlog item archived.

**Residual manual (user-side):** `docker compose up -d --build` (fresh DB gets the `status`/`resolved_at` columns from init.sql; an existing deployment needs the migration `2026-08-14-lexicon-unmapped-status.sql` applied once, or the API's schema initializer self-heals at startup). Then hard-refresh `/glossary` and `/lexicon` for a live visual check.
