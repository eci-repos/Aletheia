# Sprint 55 - Document Briefs and the End-User Wiki (Hide Internal Search Options)

**Status:** Active

## Objective

Make the Wiki genuinely useful for end users and stop exposing internal retrieval surfaces. User-facing naming: call it **Wiki** (drop "WRAGS" - too technical); "WRAGS" is reserved for internal/technical contexts only.

1. **Document Briefs** - replace community-summary wiki pages with readable, per-document "briefs": nature/purpose first, then the canonical template's ordered sections, grounded and cited, with no chunk/community/graph jargon.
2. **Hide technical options** - remove (or gate behind an admin/diagnostics flag) the raw Wiki, GraphRAG, LazyGraphRAG, and global-graph search controls under the search form so end users are not exposed to internal summaries.
3. Keep Copilot and Search Center semantic search as the primary user paths.

## Background

- `wiki_pages` today contains GraphRAG community summaries (`generated_from` = graphrag), e.g., "Community Summary: Community L0 7712a139 - Composition: 54 members, each a distinct text chunk..." - internal provenance, not end-user content (confirmed in field testing).
- Sprint 53 built the machinery for user-friendly content: `DocumentTemplateRegistry` (ordered sections per canonical), deterministic opening-chunk injection, and the ordered-section summary scaffold in `RetrievalAugmentedPromptBuilder`.
- The search/WRAGS form exposes Wiki/GraphRAG/LazyGraphRAG/global-graph search; these are internal surfaces. End users see a surface labeled **Wiki** (never "WRAGS").

## Deliverables

1. **Document Brief generation**
   - New background job (pattern: `IngestionJobService` / wiki regeneration) that, per registered document, generates a **Document Brief**: nature/purpose first (opening/Project Summary), then the canonical template's sections in order, each grounded in per-section retrieved evidence; cited; plain language.
   - Store as `wiki_pages` rows with `generated_from = 'document-brief'`, `primary_source_id` = the document, and `source_ids` = [document id]; replace/augment the existing community-summary pages for the user-facing Wiki.
   - Trigger on ingestion (after `EnsureIngestedAsync` succeeds) and via a regeneration endpoint/action.
   - Skip documents with no canonical template (ingestion gate already prevents them).

2. **Wiki surface shows briefs**
   - Wiki search/list returns document briefs first (or only); community summaries are excluded from end-user output (kept internally for graph answers / diagnostics).

3. **Hide technical options (user-facing name: Wiki)**
   - Under the search/Wiki form: hide Wiki/GraphRAG/LazyGraphRAG/global-graph search controls from end users behind an admin flag (`FeatureFlags:ShowInternalSearch` in appsettings, default false). Copilot and semantic search remain visible.
   - Remove or gate the corresponding API endpoints for non-admin users (authorization check on the wiki/global-graph controllers or a feature gate).
   - Rename user-facing labels: "Wiki" everywhere the end user sees it; keep "WRAGS" only in internal code/logs/docs.

4. **Tests**
   - Brief generation unit tests (prompt built from template + opening chunks; wiki row written with `generated_from=document-brief`).
   - Feature-flag gating tests (internal search hidden when flag false).
   - Existing suites remain green.

5. **Docs**
   - `docs/Architecture.md`: Document Briefs section (generation, storage, end-user wiki vs internal summaries).
   - `docs/AdministratorGuide.md` / `OperationsGuide.md`: the `FeatureFlags:ShowInternalSearch` flag; how to regenerate briefs.
   - AGENTS / handoff notes updated.

## Acceptance Criteria

- The Wiki (user-facing label, not "WRAGS") shows readable document briefs (nature first, template sections in order, cited) and no community/chunk jargon.
- Raw Wiki/GraphRAG/LazyGraphRAG/global-graph controls are hidden from end users (flag default false); the visible surface is labeled "Wiki".
- Briefs regenerate on ingestion and via the regeneration action.
- RAGS.UnitTests / Foundation / Repository suites green; web C# compiles.

## Out of Scope

- Improving graph algorithms or community summaries themselves (they remain internal).
- Server-side multi-tenant wiki history.

---

## Implementation Status (2026-08-04)

Implemented (see docs/File 02-Current-Sprint.md):

1. **Document Brief generation** — DocumentBriefService + SemanticKernelDocumentBriefGenerator (RAGS.Application), RetrievalAugmentedPromptBuilder.BuildDocumentBrief, background job kind DocumentBriefs in IngestionJobService, POST /api/wiki/briefs/regenerate, triggers on ingestion (after EnsureIngestedAsync and upload ingestion jobs). Briefs stored as wiki_pages rows with generated_from = 'document-brief'.
2. **Wiki surface shows briefs** — repository search/recent exclude graphrag rows and order briefs first.
3. **Internal search hidden** — FeatureFlags:ShowInternalSearch (default false) via IInternalSearchGate; gated GraphRAG/LazyGraphRAG/GraphQuery controllers and internal wiki modes; UI hides mode buttons/expansion/queue-regen; labels renamed to **Wiki**.
4. **Tests** — DocumentBriefServiceTests (3), InternalSearchGateTests (2), WikiControllerInternalSearchGateTests (5), GraphRAG/LazyGraphRAG gating tests; suites green.
5. **Docs** — Architecture, AdministratorGuide, OperationsGuide, AGENTS.md updated.