# Sprint 58 - Session Knowledge Theme Filtering

**Status:** Active

Full authority: this file. Sprint 57 (Search Center Retrieval Quality and Troubleshooting) is **committed and pushed** (`9cdc131`, `bcb59f9`, `7220987` on `origin/master`); its remaining verification is the Docker smoke test, which can run in parallel with Sprint 58 work.

## Objective

Let the end-user scope a Copilot session to a set of **knowledge themes** derived from the canonical document templates (e.g., Analysis, As-Built, As-Proposed, or any combination). Themes are chosen when the session starts (UX option #1) and displayed as chips in the session header; the selection restricts which registered documents Copilot may retrieve from for that session. No theme selection = current behavior (all documents).

## Background

- Every ingested document must match a canonical template in `docs/doc-templates` (`DocumentTemplateRegistry`, token-match on file name, e.g. `CMP 2026 - 3. RFP Analysis.docx` -> `3.0 - RFP Analysis`). Templates today carry only a name + ordered sections — there is **no theme/category concept**.
- Retrieval is already enforced deterministically at the engine: source-scoped retrieval (Sprint 51, `RetrievalRequest(query, topK, sourceId)`) and score-floor + keyword fallback (Sprint 57). A theme filter is a **session-level restriction over the set of registered sources**, enforced at the same retrieval seam, so it composes with both.
- `file_metadata` has no template/theme column today. Ingestion resolves the canonical name via the template gate (`RepositoryKnowledgeSourceIngestionService`) but does not persist it; the resolved name is only used for gate decisions and document-brief structure.
- Copilot sessions are client-side (`CopilotStateService`, localStorage) with a `ChatSession` model (RAGS.Abstractions.Models) sent to `POST /api/copilot/chat` (`CopilotController` -> `CopilotService.ChatAsync` -> `ChatRequestOptions` -> `ChatExecutionEngine`). The theme selection rides on that existing path; no new session store is needed.

## Deliverables

### 1. Theme model on canonical templates
- Each template file in `docs/doc-templates` gains a theme metadata line, e.g. first line `Theme: Analysis` (parsed by `DocumentTemplateRegistry` alongside the existing heading/section parsing; unknown/missing theme => `Uncategorized`).
- `IDocumentTemplateRegistry` gains `TryGetTheme(string fileName)` and `IReadOnlyList<string> ListThemes()` (ordered, distinct). Initial themes used by the repo: `Analysis`, `As-Built`, `As-Proposed` (others appear when new templates are added).
- `docs/doc-templates/3.0 - RFP Analysis.md` updated with `Theme: Analysis`; the theme convention documented in `docs/Development-Guidelines.md` so new templates carry a theme.

### 2. Persistence at ingestion
- `file_metadata` gains `template_name TEXT` and `theme TEXT` (idempotent migration following the Sprint 56 pattern `src/Repository.Infrastructure.PostgreSQL/Migrations/2026-08-06-file-metadata-template-theme.sql` + `init.sql` update; index on `theme`).
- `RepositoryKnowledgeSourceIngestionService` persists the canonical template name + theme on the metadata row during `EnsureIngestedAsync` (same place the template gate runs). Document updates (Sprint 56 flow) re-derive on re-ingest.
- Read-time fallback: when the column is null (pre-migration rows), the theme is derived from the file name via `DocumentTemplateRegistry.TryGetTheme` so filtering still works without a backfill job; the Reembed job (Sprint 57) re-populates columns as a natural backfill.

### 3. Session-scoped theme filter (API + retrieval enforcement)
- `ChatSession` gains `IReadOnlyList<string> ThemeFilter` (empty = all documents); `ChatPayload` passes it; `ChatRequestOptions` carries it into planning/execution.
- New `GET /api/knowledge/themes` (authenticated; extend `MetadataController` or a small `KnowledgeController`) returns `[{ theme, documentCount }]` from `file_metadata` (fallback derivation included) so the UI can render the picker with live counts.
- `RetrievalRequest` gains `IReadOnlyList<Guid> SourceIds` (nullable; supersedes single `SourceId` internally, kept backward compatible). `PgVectorStore.SearchAsync` and `SearchKeywordAsync` (Sprint 57) accept a source-id-set predicate (`source_id = ANY(...)`), so vector + keyword fallback both honor the filter.
- `ChatExecutionEngine` resolves the session theme filter to source ids once per turn (`IMetadataRepository.ListSourceIdsByThemeAsync`, new method; PostgreSQL implementation; default not-supported on the interface) and enforces it in the RAGS retrieval paths (`RunRagsRetrieveAsync`, fast-path, small-corpus, `TrySourceScopedRetrievalAsync`):
  - Named single document (Sprint 51 scope) + theme filter => **intersection** (no results if the document's theme is excluded).
  - Collection/unscoped retrieval + theme filter => retrieval restricted to the union of theme-matched sources.
  - No theme filter => unchanged behavior.
- GraphRAG/LazyGraphRAG internals and community summaries are **out of scope**; the filter applies to the RAGS retrieval paths Copilot uses by default.

### 4. Web UI - session-setup picker + header chips (UX option #1)
- **New chat flow** (`Pages/Copilot/Index.razor`): on "New chat", show a theme picker (checkboxes: Analysis, As-Built, As-Proposed, plus "All themes" default). Picker data from `GET /api/knowledge/themes`; themes with zero registered documents are still selectable (they simply match nothing) or hidden per operator config - decide during implementation, default: show all with counts.
- **Session header chips**: active themes render as chips in the conversation header (e.g., "Analysis · As-Built") with an edit affordance that reopens the picker; changes apply to subsequent turns of the same session.
- Selection persists with the session (`CopilotStateService`, bump storage key to `aletheia.copilot.session.v2`) and is included in every `ChatPayload`; `RepositoryApiClient` maps it.
- Empty selection / "All themes" sends an empty `ThemeFilter` (backward compatible).

### 5. Tests
- RAGS.UnitTests: theme->source resolution; engine restricts retrieval to theme-matched sources (single-theme and combination); intersection when a named document is excluded by the theme filter (no cross-theme leakage); keyword fallback honors the theme predicate; empty filter preserves current behavior.
- Repository.UnitTests: ingestion persists `template_name`/`theme`; `GET /api/knowledge/themes` returns counts incl. fallback derivation; `ListSourceIdsByThemeAsync` (PostgreSQL).
- Web: C#/Razor CoreCompile 0 errors; picker/chip state logic tests where feasible (RepositoryApiClient theme mapping).
- Existing suites (Repository 109 / RAGS 234 / Foundation 55) remain green.

### 6. Docs
- `docs/Architecture.md`: retrieval pipeline gains the theme-filter stage (session scope -> source set -> vector/keyword predicates).
- `docs/AdministratorGuide.md`: theme concept, template `Theme:` convention, `GET /api/knowledge/themes`.
- `docs/OperationsGuide.md`: troubleshooting "no results because session theme excluded the document".
- `docs/Development-Guidelines.md`: new templates must declare a theme.
- AGENTS.md, `docs/File 02-Current-Sprint.md`, `docs/File 03-openhands.md`, sprint handoff updated.

## Acceptance Criteria

- Templates carry themes; `3.0 - RFP Analysis` is `Analysis`; newly ingested documents persist `template_name` + `theme`; pre-existing documents resolve a theme via fallback.
- User picks themes at session creation; header shows chips; mid-session edits apply to subsequent turns; the selection persists across reloads and is sent on every chat call.
- With `ThemeFilter = [Analysis]`, Copilot retrieves only Analysis-themed sources (verifiable via source ids/citations); `[Analysis, As-Built]` retrieves the union; a named document outside the filter returns no results from that document.
- Empty `ThemeFilter` behaves exactly as before the sprint.
- Repository / RAGS / Foundation suites green; Web C#/Razor compiles.

## Out of Scope

- GraphRAG/LazyGraphRAG internals, community summaries, and the global-graph surfaces.
- A global "knowledge scope" widget that filters Search Center / Wiki / Browse (those stay unfiltered).
- Rerankers, multi-tenant/security scoping, per-document ACLs.
- New queue providers or session stores (sessions remain client-side state).

---

## Implementation Status (2026-08-06)

- Sprint file created; Sprint 57 closed out (commit `7220987` pushed). No Sprint 58 implementation yet.
## Implementation Status (2026-08-06)

**Deliverables 1-4 implemented; D5 verification green; D6 docs updated.**

- **D1 - Theme model**: `docs/doc-templates/3.0 - RFP Analysis.md` declares `Theme: Analysis`; `DocumentTemplateRegistry` parses the first-line `Theme:` metadata (`TryGetTheme` / `ListThemes`, `Uncategorized` constant); theme convention documented in `docs/Development-Guidelines.md`.
- **D2 - Persistence**: migration `src/Repository.Infrastructure.PostgreSQL/Migrations/2026-08-06-file-metadata-template-theme.sql` + `init.sql` (`template_name`, `theme`, index on `theme`); `FileMetadata` surfaces both; `IMetadataRepository.SetTemplateAsync` / `ListThemeRowsAsync` (+ PostgreSQL implementation); `RepositoryKnowledgeSourceIngestionService` persists template + theme after the canonical-gate passes.
- **D3 - Session filter + retrieval enforcement**: `RetrievalRequest.SourceIds` (set predicate in `PgVectorStore.SearchAsync`/`SearchKeywordAsync` via `SearchBySourcesAsync` + `source_id = ANY(...)`); `KnowledgeThemeService` (singleton) + `GET /api/knowledge/themes`; `ChatSession.ThemeFilter` -> `ChatPayload`/`PlanPayload` -> `ChatRequestOptions`/`ChatPlanRecord` (preserved through plan approval); engine enforces in RAGS paths + intersects Sprint 51 single-document scope + post-filters tool-path results; direct Copilot path (`SemanticKernelCopilotService`) intersects the named source with the theme scope.
- **D4 - Web UI**: Copilot "New chat" opens the theme picker (from `GET /api/knowledge/themes`, with document counts); session header shows theme chips with an Edit control; selection persists with the session (`CopilotStateService` storage key v2) and is sent on every plan/chat call (`RepositoryApiClient.PlanChatAsync`/`ChatAsync`).
- **D5 - Tests**: RAGS 249 (theme registry, KnowledgeThemeService resolution/counts, RagsService source-set + keyword fallback, engine theme restriction, plan carries theme), Repository 113 (ingestion persistence, themes endpoint), Foundation 55; Web CoreCompile 0 errors.
- **Remaining**: Docker smoke test (upload -> ingest -> themes endpoint -> theme-scoped Copilot session vs all-themes session), commit.