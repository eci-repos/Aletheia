# Sprint 58 - Session Handoff (2026-08-06)

Status: **Deliverables 1-4 implemented; tests green; uncommitted.**

## What is done (working tree, uncommitted)

- **D1 - Theme model**: `docs/doc-templates/3.0 - RFP Analysis.md` first line `Theme: Analysis`; `IDocumentTemplateRegistry.TryGetTheme` / `ListThemes`; `DocumentTemplateRegistry` parses the `Theme:` line (missing => `Uncategorized`); `docs/Development-Guidelines.md` documents the convention.
- **D2 - Persistence**: migration `src/Repository.Infrastructure.PostgreSQL/Migrations/2026-08-06-file-metadata-template-theme.sql` + `init.sql` (`template_name`, `theme`, index); `FileMetadata.TemplateName`/`Theme`; `IMetadataRepository.SetTemplateAsync` + `ListThemeRowsAsync` (+ `FileThemeRow`); PostgreSQL implementation; `RepositoryKnowledgeSourceIngestionService` persists template + theme right after the canonical-gate passes.
- **D3 - Session filter + retrieval enforcement**:
  - `RetrievalRequest.SourceIds` (set scope; empty set = no sources match); `ISourceFilteredVectorStore.SearchBySourcesAsync` + `IVectorStore.SearchKeywordAsync(query, topK, sourceIds, ct)` overload; PgVectorStore `source_id = ANY(...)` on both paths.
  - `KnowledgeThemeService` (singleton) + `KnowledgeThemeCount` model + `GET /api/knowledge/themes`.
  - `ChatSession.ThemeFilter`, `ChatPayload.ThemeFilter`, `ChatRequestOptions.ThemeFilter`, `ChatPlanRecord.ThemeFilter`, `PlanPayload.ThemeFilter`; `CreatePlanAsync(..., themeFilter)`; `InMemoryChatPlanRepository.UpdateStatusAsync` preserves ThemeFilter (fix).
  - Engine: `ResolveThemeSourceIdsAsync`; RAGS paths (RunRagsRetrieveAsync / fast-path / small-corpus) pass `sourceIds:`; `TrySourceScopedRetrievalAsync` intersects single-document scope; tool-path results post-filtered before synthesis.
  - Direct chat (`SemanticKernelCopilotService`): theme -> source ids; named source intersected (outside themes => empty).
- **D4 - Web UI**: theme picker on "New chat" (from `GET /api/knowledge/themes` with counts), header chips + Edit, persisted via `CopilotStateService` (storage key v2), sent on `PlanChatAsync`/`ChatAsync`.
- **Docs**: Architecture (retrieval pipeline theme stage), AdministratorGuide (Knowledge Themes), OperationsGuide (troubleshooting), Development-Guidelines (theme convention), File 02, File 03, sprint file.

## Verification

- RAGS.UnitTests 249 passed; Repository.UnitTests 113 passed; Foundation 55 passed; Web CoreCompile 0 errors.
- Flaky pre-existing timing test `Engine_honors_step_timeouts` passed in this run (was seen flaky under parallel load previously).

## Next

1. Docker smoke test: upload doc -> ingest -> `GET /api/knowledge/themes` shows Analysis + count; Copilot session with Analysis selected retrieves only that document; All-themes session unchanged; header chips + Edit flow.
2. Commit (in the user's terminal): `git add -A` then commit D1-D5 as Sprint 58 implementation.