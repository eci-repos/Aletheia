# Sprint 71 - Lexicon Governance and Glossary Surface

**Status:** Active (2026-08-14)

Full authority: `docs/sprints/Sprint-71 - Lexicon Governance and Glossary Surface.md` (created 2026-08-14). This file is the active implementation authority; the referenced sprint file defines the authorized scope.

Sprint 70 (Normalized Lexicon / Grounded Semantic Extraction) is **complete, committed, and pushed** on `origin/master` (`229229d`).

## Objective

Close the governance loop Sprint 70 opened: a glossary/lexicon for a given document domain that **end users can view and download** and **admins can extend and manage**. Two surfaces, one sprint — the admin management surface (browse concepts + aliases, add/remove aliases, add concepts, review unmapped terms) is the growth mechanism; the end-user read-only glossary (per-domain concept list + verified facts, downloadable as CSV/JSON) is the surfacing. The connective tissue is **`template_scope` enforcement**: a concept with a template scope applies only to documents of that template; unscoped concepts stay global.

## Authorized Work (summary - see sprint file for details)

1. **Admin lexicon management API + repository methods:** `ILexiconRepository` gains `DeleteConceptAsync`/`ResolveUnmappedTermAsync`/`GetAllFactsAsync`; `lexicon_unmapped_terms` gains `status`/`resolved_at` (migration `2026-08-14-lexicon-unmapped-status.sql` + `init.sql` + `PostgreSqlLexiconSchema`); `LexiconController` (Repository.API) — concepts read/upsert/delete, unmapped list/resolve; admin writes invalidate `LexiconProvider`.
2. **`template_scope` enforcement in concept matching:** `FactVerifier.Verify` + `IFactExtractionService.ExtractAsync` / `GroundedFactExtractionService` take an optional `templateName`; scoped concepts apply only to matching templates; `EnsureIngestedAsync` passes the canonical template name.
3. **End-user glossary view:** `GET /api/lexicon/glossary?template=` (concepts + facts with source names via `IMetadataRepository`); `Pages/Glossary/Index.razor` at `/glossary` with domain filter + download; nav entry.
4. **Download/export (CSV + JSON):** `GET /api/lexicon/glossary/export?format=csv|json&template=` — file download.
5. **Tests + docs:** RAGS 343 (+5) / Repository 151 (+13) / Web 88 (+4) / Foundation 55; build 0 errors; docs updated; backlog item archived.

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

## Progress

### Sprint 71 — lexicon governance and glossary surface (2026-08-14)

**Implemented.** See the Sprint 71 sprint file "Implementation Status" for full detail:

- **Item 1 (admin API + repository):** `ILexiconRepository` + `PostgreSqlLexiconRepository` gain `DeleteConceptAsync`, `ResolveUnmappedTermAsync`, `GetAllFactsAsync`; `GetUnmappedTermsAsync` returns pending only. `lexicon_unmapped_terms` gains `status`/`resolved_at` (migration `2026-08-14-lexicon-unmapped-status.sql` + `init.sql` + `PostgreSqlLexiconSchema`). `LexiconController` (Repository.API): `GET /api/lexicon/concepts?template=` (authenticated), `PUT`/`DELETE /api/lexicon/concepts` (admin), `GET /api/lexicon/unmapped` + `POST /api/lexicon/unmapped/resolve` (admin); admin writes call `_lexiconProvider.Invalidate()`. Admin UI: `Pages/Lexicon/Index.razor` at `/lexicon` (admin-gated, Management nav group) — browse/add/edit/delete concepts + dismiss unmapped terms.
- **Item 2 (template_scope enforcement):** `FactVerifier.Verify` + `IFactExtractionService.ExtractAsync` / `GroundedFactExtractionService` take an optional `templateName`; `FactVerifier.IsApplicable(concept, templateName)` is the single source of truth (unscoped always applies). `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` passes the matched canonical template name. Out-of-scope hints behave like unknown hints (verifiable value stored as raw text + recorded unmapped).
- **Item 3 (end-user glossary):** `GET /api/lexicon/glossary?template=` joins facts with source names via `IMetadataRepository`; `Pages/Glossary/Index.razor` at `/glossary` (concepts + aliases + verified facts with source/page, template filter, download buttons); nav entry after Copilot (`icon-glossary`).
- **Item 4 (export):** `GET /api/lexicon/glossary/export?format=csv|json&template=` returns `File(...)` downloads; Web buttons via `RepositoryApiClient.ExportGlossaryAsync` → `DotNetStreamReference` + `downloadFileFromStream`.
- **Item 5 (tests + docs):** RAGS 343 (+5) — `FactVerifierTests` template-scope cases (4) + `GroundedFactExtractionServiceTests` pass-through (1); Repository 151 (+13) — `LexiconControllerTests` (13: concepts read/filter, upsert/delete + invalidate, unmapped list/resolve, glossary join + scope filter, CSV/JSON export); Web 90 (+6) — `LexiconBindingTests` (unmapped status columns, glossary page, glossary nav entry, admin `/lexicon` page + gate, admin nav entry, client methods). Foundation 55 unchanged; build 0 errors; docs updated; backlog item archived.

**Residual manual (user-side):** `docker compose up -d --build` (fresh DB gets the `status`/`resolved_at` columns from init.sql; an existing deployment needs the migration `2026-08-14-lexicon-unmapped-status.sql` applied once, or the API's schema initializer self-heals at startup). Then hard-refresh `/glossary` (and `/lexicon` for the admin surface) for a live visual check.

---

## Sprint 70 progress log (2026-08-14) — completed

### Sprint 70 — normalized lexicon (grounded semantic extraction) (2026-08-14)

**Implemented, committed, and pushed (`229229d`).** See the Sprint 70 sprint file "Implementation Status" for full detail:

- **Item 1 (lexicon data model + repository):** `LexiconConcept`/`DocumentFact`/`ProposedFact`/`UnmappedTerm` + `LexiconSeedData` (5 seeded concepts: due_date, budget, page_limit, vendor, submission); `ILexiconRepository` → `PostgreSqlLexiconRepository` (Dapper; `SaveFactsAsync` replaces on re-ingest); tables in `init.sql` + migration `2026-08-14-lexicon-and-facts.sql` (idempotent, seeded); `PostgreSqlLexiconSchema` + hosted initializer registered in `Program.cs`.
- **Item 2 (grounded fact extraction):** `SemanticKernelFactProposer` (LLM propose, span-quoting prompt, empty-on-failure), `FactValueParser` (date/currency/number/text), `FactVerifier` (span-existence + value-parse fidelity gate, page anchoring via `WhitespaceCollapser`), `GroundedFactExtractionService` (orchestration + unmapped-term recording); wired best-effort into `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` (never blocks ingestion).
- **Item 3 (query-time concept expansion):** `LexiconExpander` (alias-family expansion, original query kept) + `LexiconProvider` (cached, invalidatable); `RagsService.RetrieveAsync` applies it after `QueryExpander` when an `ILexiconProvider` is present (optional ctor param — existing fakes compile).
- **Item 4 (tests + docs):** RAGS 338 (+36) — `LexiconExpanderTests` (6), `FactValueParserTests` (8), `FactVerifierTests` (7), `GroundedFactExtractionServiceTests` (5), `RagsServiceTests` (+2 lexicon wiring); Web 84 (+3) — `LexiconBindingTests` (tables in migration + init, seed mirrors `LexiconSeedData`). Repository 138 / Foundation 55 unchanged; build 0 errors; docs updated; backlog item archived.

**Residual manual (user-side):** `docker compose up -d --build` (fresh DB gets the tables + seed from init.sql; an existing deployment needs the migration `2026-08-14-lexicon-and-facts.sql` applied once, or the API's schema initializer self-heals at startup). Then re-upload the CMP 2026 RFP (or run a repair job) so grounded facts are extracted, and re-ask "What is the submission due date for the CMP 2026 RFP?" — the query now embeds the due-date alias family, so "Bid due" / "Proposal Due Date" documents should surface.

---

## Sprint 68 progress log (2026-08-13) — completed

### Sprint 68 — query expansion for acronyms (2026-08-13)

**Implemented, committed, and pushed.** See the Sprint 68 sprint file "Implementation Status" for full detail:

- **Item 1 (QueryExpander):** `src/RAGS.Application/QueryExpander.cs` — static class with a public `Expansions` dictionary (17 domain acronyms: AI, GenAI, RFP, RFI, ML, LLM, NLP, API, SOW, SLA, KPI, POC, MVP, OCR, PDF, SQL, RAG) and `Expand(string)` — single-pass, longest-first, word-boundary regex, case-insensitive, keeps the original token.
- **Item 2 (wire-through):** `RagsService.RetrieveAsync` embeds `QueryExpander.Expand(request.Query)`; the keyword fallback keeps `request.Query` (whole-string ILIKE match).
- **Item 3 (tests + docs):** RAGS 302 (+9) — `QueryExpanderTests` (7) + `RagsServiceTests` (+2: expanded query reaches the embedding provider, keyword fallback uses the original). Foundation 55 / Repository 134 / Web 76 unchanged; build 0 errors; docs updated; backlog item archived.

**Residual manual (user-side):** hard-refresh `/search` and `/copilot`, then re-ask the broad question ("provide a summary of RFP opportunities related to AI") to confirm the AI RFP is now retrieved.

---

## Sprint 67 progress log (2026-08-13) — completed

### Sprint 67 — source verification view in document (2026-08-13)

**Implemented, committed, and pushed.** See the Sprint 67 sprint file "Implementation Status" for full detail:

- **Item 1 (chunk source locator):** `Chunk` gains nullable `PageNumber`/`OffsetInPage`; `ChunkingPipeline` gains a page-boundary overload (`TextPage` record); `UploadedFileTextExtractor` gains a page-aware PDF path (PdfPig); `page_number` added to the embeddings schema (idempotent migration `2026-08-13-embeddings-page-number.sql` + `init.sql` + `PgVectorSchema`); populated via the lightweight reembed flow.
- **Item 2 (preview endpoint):** `GET /api/files/{id}/preview` (optional `?version=`) streams the original blob inline — PDF → raw bytes, text/docx → extracted text + page markers, unsupported → 415. `IMetadataRepository.GetByFileIdAsync` added (default no-op, PostgreSQL override).
- **Item 3 (in-app document viewer):** `Pages/Document/View.razor` at `/document/{id}?page=&chunk=` — PDF.js renderer (text layer) for PDF, extracted-text renderer with page markers for other types.
- **Item 4 (passage highlight + auto-scroll):** chunk leading phrase highlighted in the PDF.js text layer or the text preview; scroll-to-highlight with page-jump fallback — never a hard error.
- **Item 5 (wire-through):** Search Center result cards render "View in document (p. N)"; Copilot `[N]` citations become viewer links via `ChatCitation`/`BuildCitations`; `RepositoryApiClient.PreviewAsync`.
- **Item 6 (tests + docs):** RAGS 293 (+3) / Repository 134 (+4) / Web 76 (+8) / Foundation 55 green; `dotnet build Aletheia.slnx` succeeds (0 errors); docs updated; backlog item archived.

**Residual manual (user-side):** hard-refresh `/search` and `/copilot` for a live visual check; optional Docker smoke pass (upload a PDF → search → open the passage in `/document/{id}`).

---

## Sprint 66 progress log (2026-08-13) — completed

## Objective

Remove the **Metadata** side-menu item from `NavMenu.razor`. The page is a file-picker that opens the metadata editor, and **Browse** already provides the same flow via its ✎ Edit action (deep-link to `metadata?fileId=...`). The standalone nav item is a redundant, weaker entry point. The `/metadata` page, its route, and Browse's Edit deep-link stay untouched.

## Authorized Work (summary - see sprint file for details)

1. **Remove the Metadata nav item:** delete the `NavLink href="metadata"` block from `src/Aletheia.Web/Layout/NavMenu.razor`. No page, route, or API changes.
2. **Binding test:** assert `NavMenu.razor` no longer contains `href="metadata"` and `Browse.razor` still contains `metadata?fileId=` (Edit action preserved).
3. **Docs:** File 02/03, AGENTS, CLAUDE.md, sprint file; backlog item moved to `docs/backlog/archive/` when complete.

## Acceptance Criteria

- The Metadata entry is gone from the side nav; `/metadata` still resolves and Browse's ✎ Edit action still opens the editor.
- Web unit suite green; `dotnet build Aletheia.slnx` succeeds.

## Out of Scope

- Removing/renaming the `/metadata` route or `MetadataEditor.razor`.
- Changing Browse's Edit action or the metadata editor.
- The "Searching…" hang diagnostic (API availability, not code).

---

## Progress

### Sprint 66 — remove redundant Metadata nav item (2026-08-13)

**Implemented, committed, and pushed.** See the Sprint 66 sprint file "Implementation Status" for full detail:

- **Item 1 (remove nav item):** the `NavLink href="metadata"` block was deleted from `NavMenu.razor`; the `/metadata` page/route and Browse's ✎ Edit deep-link are untouched.
- **Item 2 (binding test):** `NavMenuBindingTests` (3) — nav entry gone, Browse still deep-links to `metadata?fileId=`, page route intact.
- **Item 3 (docs):** File 02/03, AGENTS, CLAUDE.md, sprint file updated; backlog item archived.

**Verification:** Foundation 55 / Repository 130 / RAGS 290 / Web 64 green; `dotnet build Aletheia.slnx` succeeds (0 errors). No sprint currently active — next promotion will set it.

### Post-Sprint 66 nav grouping (2026-08-13)

Per the project owner, Governance and Settings were already at the bottom of the side nav; added a divider + muted **Management** label above Governance in `NavMenu.razor` so the primary surfaces (Dashboard → Copilot) read as one group and the admin/management items sit clearly apart. Hidden when the sidebar is collapsed. Web 65 (+1, `NavMenuBindingTests.Nav_menu_groups_management_items_below_a_divider`); build 0 errors.

### Post-Sprint 66 Dashboard card tints (2026-08-13)

Per the project owner, the Dashboard action cards (Upload/Browse/Search Center/Wiki/Copilot) got very light pastel tints: each card carries a `dashboard-action-<name>` modifier class styled in the new `Dashboard.razor.css` — a light background wash, a 3px colored top border, and a darker shade on the card title (soft green/blue/amber/violet/teal). Text stays dark for contrast; buttons unchanged. Web 67 (+2, `DashboardBindingTests`); build 0 errors.

Follow-up: the Dashboard now shows a **loading indicator** while `_recentFiles` is still `null` (the body only renders after the first API call returns) — a Bootstrap spinner + "Loading repository data…" in a `.dashboard-loading` block, so a slow refresh no longer looks like a blank page. Web 68 (+1, `DashboardBindingTests.Dashboard_shows_loading_indicator_while_data_loads`).

---

## Sprint 65 progress log (2026-08-13) — completed

### Sprint 65 — wiki markdown/HTML view tabs (2026-08-13)

**Implemented, committed, and pushed.** See the Sprint 65 sprint file "Implementation Status" for full detail:

- **Item 1 (shared markdown renderer):** `MarkdownRenderer.ToHtml(string)` in `src/Aletheia.Web/Services/MarkdownRenderer.cs` — extraction of Copilot's former private `RenderMarkdown` helpers (headings/tables/lists/paragraphs/inline bold+code), all HTML-encoded before formatting; emitted classes renamed `copilot-table*` → `md-table*`. Copilot's `RenderMarkdown` keeps only its JSON `<pre class="copilot-json">` branch and delegates otherwise.
- **Item 2 (Wiki View/Source tabs):** `Wiki.razor` shows a View/Source tab bar (default View); View renders the summary via `MarkdownRenderer.ToHtml` as a `MarkupString`, Source shows raw md in `<pre class="wiki-source-view">`; ephemeral `_viewMode` page state, no API/wire changes.
- **Item 3 (tests):** Web 61 (+15) — `MarkdownRendererTests` (11) + `WikiViewTabsBindingTests` (4).

**Verification:** Foundation 55 / Repository 130 / RAGS 290 / Web 61 green; `dotnet build Aletheia.slnx` succeeds (0 errors). Backlog item archived.

---

## Sprint 64 progress log (2026-08-11) — completed

### Sprint 64 — theme-aware graph retrieval (2026-08-11)

**Implemented, committed, and pushed.** See the Sprint 64 sprint file "Implementation Status" for full detail:

- **Item 1 (theme scope on graph retrieval):** `sourceIds` params on all three graph services; `GraphThemeScope` helper (`TryGetSourceId`, `IsInScope`, `FilterNodes`, `ToAllowSet`, `CommunityHasMemberInScope`); `GraphRagService` filters resolved entities + multi-hop expansion nodes and scopes semantic fallback / entity-expansion `RetrievalRequest`s; `LazyGraphRagService` filters corpus seed sources and scopes fallback / expansion requests; `GlobalGraphSearchService` builds a node→source map via `IGraphProvider.GetNodesAsync()` and filters communities with match-any semantics (returns `Failure("No communities in the selected themes.")` when scoped and empty).
- **Item 2 (API + Web wiring):** `?themes=` on both graph controllers' `Retrieve` + `GlobalSearch`; `RepositoryApiClient` appends `&themes=`; `SearchCenter.razor` passes `_selectedThemes` to graph-mode retrieve calls (WRAGS note now reads "Theme scope does not apply to WRAGS search.").
- **Item 3 (tests):** RAGS 289 (+8) — theme-scoped entity/community filtering, corpus-seed filtering, source-id flow to semantic fallback, controller themes pass-through. All fakes updated for the new signatures.

**Verification:** RAGS 289 / Repository 130 / Foundation 55 / Web 46 green; `dotnet build Aletheia.slnx` succeeds (only pre-existing AngleSharp NU1902 warning).

---

## Sprint 63 progress log (2026-08-11) — completed

### Sprint 63 items 1 + 2 — corpus index persistence + batch ingest (2026-08-11)

**Implemented, committed, and pushed (`df7627d`).** See the Sprint 63 sprint file "Implementation Status" for full detail:

- **Item 1 (corpus index persistence):** `ICorpusIndexRepository` → `PostgreSqlCorpusIndexRepository` (Dapper, `lazygraphrag_corpus_documents` + `lazygraphrag_corpus_terms`, migration `2026-08-11-lazygraphrag-corpus-index.sql` + `init.sql` in sync); `CorpusDiscoveryIndex` loads the persisted corpus at startup and persists write-through (best-effort — a persistence failure never fails ingestion); `AddSingleton<ICorpusIndexRepository, PostgreSqlCorpusIndexRepository>()` in `Program.cs`.
- **Item 2 (batch ingest):** `IGraphProvider` batch methods (`CreateNodesAsync`/`CreateRelationshipsAsync`/`UpdateNodesAsync`, default interface impls fall back to per-item calls so existing fakes keep compiling); `Neo4jGraphProvider` UNWIND implementations grouped by label/type; both full-ingest paths (`UploadedContentKnowledgeIndexer.PersistGraphIntelligenceAsync` + `GraphRagService.IngestAsync`) refactored into 4 phases with `SemaphoreSlim(MaxLlmConcurrency = 4)`; community re-clustering gated on `!sourceExists`.

**Verification:** RAGS 281 (+9) / Repository 130 / Foundation 55 / Web 46 green; `dotnet build Aletheia.slnx` succeeds. Optional Docker smoke test (restart corpus survival + batched-write ingest) is user-side.

---

## Sprint 62 progress log (2026-08-11) — completed

### Sprint 62 items 1 + 2 — reembed parity + soft deadline (2026-08-11)

**Implemented, committed, and pushed (`26995d9`).** See the Sprint 62 sprint file "Implementation Status" for full detail:

- **Item 1 (reembed parity):** `KnowledgeIndexMode` enum (`Full`/`Lightweight`) in `RAGS.Abstractions.Models`; `EnsureIngestedAsync` takes `mode = Full` and branches to `IndexLightweightAsync` when Lightweight; `RunReembedJobAsync` passes `Lightweight` (repair/chat keep `Full`).
- **Item 2 (soft deadline):** `GraphRagService.RetrieveAsync` distinguishes deadline-fires from caller-cancel — deadline degrades to best-effort semantic retrieval under a ~10s secondary deadline, returning Success with trace strategy `semantic-timeout-fallback` + steps `deadline-exceeded`/`semantic-fallback`; caller-cancel and other exceptions still fail. Optional `budgetFactory` ctor param for tests.
- **Smoke-test follow-up fix (`88164e4`):** the degrade now also covers the **returned-Failure** path — `PgVectorStore` converts a cancelled vector search into a returned `Failure` (not a thrown `OperationCanceledException`), so the deadline-fires check is applied to `baseResults.IsFailure` too, via a shared `RunSemanticTimeoutFallbackAsync` helper. Without it a deadline during the base semantic retrieval still hard-failed with HTTP 400.

**Verification:** Repository 130 (+1) / RAGS 290 (+3) / Foundation 55 / Web 46 green; `dotnet build Aletheia.slnx` succeeds. **Docker smoke test RUN 2026-08-11 — PASS:** reembed completed in ~70s (vs 40+ min pre-Sprint 62); 16 concurrent GraphRAG retrievals under LLM saturation returned all HTTP 200 — 6 hit the 30s deadline and degraded to `semantic-timeout-fallback` with real results, zero HTTP 400 (pre-fix: 3/8 were 400).

---

## Sprint 61 progress log (2026-08-10/11) — completed

### Sprint 61 item 1 — modal approval prompt (2026-08-10)

**Implemented.** The plan-approval prompt is no longer hidden behind the Activity/Chats panels or a collapsed Execution column:

- `Index.razor` renders `PlanPreview` inside a centered modal overlay (`.copilot-approval-backdrop` / `.copilot-approval-modal`, `z-index: 1050` — above the panels' `20`/`21`) whenever a plan is awaiting approval/run (`IsPlanPreviewVisible && _pendingPlan?.Status == ChatPlanStatus.Proposed`). The modal reuses the existing `PlanPreview` component (Run/Revise/Cancel), so there is no duplicated markup; the in-context plan preview stays in the Execution column.
- `SendChat()` now auto-expands a collapsed Execution column on submit, so the approval prompt and later progress are always visible.
- CSS added to `Index.razor.css` (fixed backdrop, centered card, `max-height` + scroll).

**Verification:** `dotnet build src/Aletheia.Web/Aletheia.Web.csproj` 0 warnings/0 errors; Aletheia.Web.UnitTests 39/39 green (binding tests still pass). Committed `4d10561`.

### Sprint 61 items 2+3+4 — settings foundation + approval preference + admin override (2026-08-10)

**Implemented and pushed (`793fc52`).** See the sprint file for full detail:

- **Item 2 (settings foundation):** `app_settings` + `user_settings` tables in `init.sql` + idempotent migration `2026-08-10-app-user-settings.sql`; `ISettingsRepository` → `PostgreSqlSettingsRepository` (Dapper `ON CONFLICT` upsert) → `ISettingsService` → `SettingsService` (singleton, in-memory caching, typed `GetBool/SetBool`); `GET/PUT /api/settings` (Administrator) + `/api/settings/me` (authenticated), caller id from JWT `NameIdentifier`.
- **Item 3 (approval preference):** `copilot.requireApproval` per-user, default true; modal "Don't ask again" checkbox writes the preference; when off the client auto-approves + executes (`SendChat` → `ApprovePlan`).
- **Item 4 (admin override):** `copilot.requireApproval.force` global (default false) forces approval for opted-out users, never for non-expensive plans. Keys in `Aletheia.RAGS.Abstractions.Configuration.ChatApprovalSettings`. `ChatPlanApprovalService.CreatePlanAsync` takes the caller's userId and applies `base && (userPrefersApproval || adminOverride)`.

**Verification:** RAGS 270 / Repository 129 / Web 44 / Foundation 55 green; build succeeds.

### Sprint 61 item 5 — admin Settings page (2026-08-10)

**Implemented and pushed (`f8f5292`).** `Pages/Settings/Index.razor` at `/settings` — **My Preferences** (own `copilot.requireApproval` toggle, any authenticated user) + **Global Settings (Administrator)** card (`copilot.requireApproval.force` toggle) rendered only via `AuthorizeView Roles="Administrator"`; loads/saves via the item 2 settings endpoints. Admin-only **Settings** entry added to the NavMenu (`.icon-settings`). Gating matches the Governance pattern (API enforces admin; UI hides the admin card/nav entry for non-admins while every user edits their own preference).

**Verification:** Aletheia.Web.UnitTests **46** (was 44, +2) green; RAGS 270 / Repository 129 / Foundation 55 unchanged; build succeeds.

### Sprint 61 complete (2026-08-11)

All 5 items implemented, committed, and pushed to `origin/master`. Unit suites green: RAGS 270 / Repository 129 / Foundation 55 / Aletheia.Web.UnitTests 46; `dotnet build Aletheia.slnx` succeeds. The parallel Sprint 60 Docker smoke test was completed 2026-08-10 (committed `3c5b509`).

---

## Sprint 59/60 progress log (2026-08-07)

### Sprint 59 (completed) — Canonical Gate Softening, Multi-Theme, and Shared Theme Scope

Committed and pushed as `c151ea2`. Full details in the Sprint 59 sprint file and the pre-Sprint-62 history.

### Sprint 60 implementation (2026-08-07) — all four deliverables implemented

See `docs/sprints/Sprint-60 - GraphRAG and LazyGraphRAG Quick Wins.md` "Smoke Test Results (2026-08-10)" for the verified traces, concurrency checks, hard-deadline behavior, and reembed timing that motivated Sprint 62 items 7 + 8.

**Verification:** RAGS.UnitTests **265 passed**; `dotnet build Aletheia.slnx` succeeds; Aletheia.Web.UnitTests 6 pre-existing failures fixed 2026-08-10 (all stale tests — see below).

### Post-implementation web-test fix (2026-08-10)

The 6 pre-existing `Aletheia.Web.UnitTests` failures fixed — all **stale tests, no code regressions**:

- `RepositoryApiClientUploadTests` ×4 — fake `HttpClient` missing the `BaseAddress` production always sets (`Program.cs:27`); fake now sets `http://localhost`.
- `CopilotStateServiceTests.ClearAsync` — asserted storage key `v1`; intentionally bumped to `v2` in `dfc9d1b` (Sprint 58). Test now asserts `v2`.
- `CopilotIndexBindingTests.Wiki_shows_all_rags_mode_buttons` — asserted `>WRAGS</button>`; renamed to `>Wiki</button>` in Sprint 55.

**Verification:** Aletheia.Web.UnitTests **39 passed**; full solution build 0 errors; RAGS 265 / Repository 121 / Foundation 55 green. Committed with the Sprint-16 sprint-file filename normalization (space → dash).

### Post-implementation chat fix (2026-08-07)

Smoke-test report "Chat does not work at all" traced to the Copilot restore path: after a page reload the Web page restored a pending plan and polled `GET /api/copilot/plans/{id}/progress`, which returned **404** for a plan with no execution job yet — the client then polled every 2s **forever**. Fixed:

- API `GetPlanProgress`: a plan without an execution job now returns **200** with `JobId = Guid.Empty` (not-started state) instead of 404; "plan not found" still 404s.
- Web `Index.razor`: the polling loop treats `JobId == Guid.Empty` as "not started" — clears stale restored execution state, keeps the plan preview so **Run** works — and stops after 3 consecutive no-progress polls instead of looping indefinitely.

Verified end-to-end via curl; RAGS 251 / Repository 121 / Foundation 55 green; Web.UnitTests still the same 6 pre-existing failures at the time. Containers rebuilt. **Browser action required: hard refresh (Ctrl+F5)** to load the new WASM bundle.

### Post-implementation graph UX fix (2026-08-07)

Smoke-test feedback: the Graph Explorer "jumps around" while the layout runs and gives no feedback. Fixed:

- **Visible "preparing graph" state**: `GraphExplorer.razor` spinner + staged status line over the canvas; Refresh/Import/Fit/Re-layout/Spread/Find Path disabled during load.
- **Render once, don't re-layout**: `window.initGraph` accepts `dotNetRef` + `preservePositions`; on scope changes the graph re-renders keeping node positions (`randomize: false`); JS hooks `layoutstop` → `OnGraphLayoutSettled` clears the overlay.

Contract: `initGraph(containerId, nodes, edges, dotNetRef, preservePositions)`; page owns a `DotNetObjectReference<GraphExplorer>` disposed in `Dispose`. Web project builds clean.
