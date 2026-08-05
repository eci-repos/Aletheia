# Sprint 49 - Wiki UPlan (Template Based Wiki Ingestion)

**Status:** Planned

## Objective

Implement a “Wiki‑UPlan” that automatically creates WRAGS wiki pages from ingested documents using markdown templates located in `docs/doc-templates`. The goal is to make every uploaded source immediately visible in the wiki, keep the knowledge graph in sync, and provide a reliable regeneration pipeline.

## Background

Current ingestion creates a lightweight graph seed for a source but does not generate a wiki page. The Phase 21 handoff describes WRAGS wiki functionality, yet the mapping from document templates to wiki pages is missing. Without template‑driven pages, operators must manually create and edit wiki content, which defeats the purpose of automated background operations. This sprint will close the gap by introducing a template provider, rendering engine, and persistence flow that ties into the existing ingestion job.

## Authority

The repository is the source of truth. All work must respect the **Singleton Registration Rule** (`IGraphProvider` and any dependent services are singletons) and use the **Result<T> pattern** for error handling. The existing WRAGS wiki service (`IWragsWikiService`) and `IWikiPageRepository` shall be used.

## Deliverables

- **Template Provider** (`IWikiTemplateProvider`) that reads markdown files from `docs/doc-templates` and caches them (singleton).
- **Wiki Page Generator** (`IWikiPageGenerator`) that fills placeholders (`{{Title}}`, `{{SourceId}}`, `{{Summary}}`, etc.) using Scriban or equivalent.
- **Ingestion Job Extension** that, after lightweight graph indexing, selects the appropriate template, renders the page, persists via `IWikiPageRepository`, creates a `WikiPage` graph node, and adds a `found_in` edge to the source node.
- **Graph Sync Updater** (`WikiIngestionGraphUpdater`) to add wiki nodes/edges to Neo4j/GraphProvider.
- **Automatic Regeneration Job** enqueued after creation to run the full WRAGS pipeline (entity extraction, summary generation, backlink creation).
- **Configuration** (`appsettings.json` → `Wiki.TemplateFolder`) and DI registration for the new services.
- **Unit Tests** for template discovery, rendering, and failure handling.
- **Integration Test** exercising end‑to‑end ingestion → wiki page → graph node → regeneration job.
- **Documentation** updates in `docs/Phase21-Background-Operations-Handoff.md` and a new section in `docs/README` describing the wiki‑UPlan.
- **Activity Panel** stage entry “Generate Wiki Page (template X)” visible during ingestion.

## Requirements

### 1. Template Discovery
- Load all `*.md` files under the configured folder at startup.
- Expose `TryGetTemplate(string name, out string markdown)`.
- Fallback to `DefaultWikiTemplate.md` when no specific template matches.

### 2. Template Rendering
- Use Scriban (or similar) to replace `{{placeholder}}` tokens.
- Required placeholders: `Title`, `SourceId`, `CreatedAt`, `CreatedBy`, `Keywords`, `Summary`, `Sections` (optional).
- Return `Result<string>`; on failure log a warning and fall back to the default template.

### 3. Ingestion Job Integration
- Extend `IngestionJobService` to call the generator after lightweight indexing.
- Persist the page via `IWikiPageRepository.UpsertAsync`.
- Return `Result.Success()` or `Result.Failure(string)` to drive job status.

### 4. Graph Node / Edge Creation
- Create a `GraphNode` of type `WikiPage` with properties: `sourceId`, `title`, `status` (`Generated`).
- Add `GraphEdge` with `RelationshipType = "found_in"` from source node → wiki node.
- Preserve singleton registration for the updater.

### 5. Regeneration Job
- Enqueue `RegenerateWikiPageJob` (existing job type) with `TriggeredBy = IngestionJobId`.
- The job runs WRAGS pipeline to enrich the page with entity nodes, backlinks, etc.

### 6. Configuration & DI
- Add `"Wiki": { "TemplateFolder": "docs/doc-templates" }` to `appsettings.json`.
- Register services as singletons:
  ```csharp
  services.AddSingleton<IWikiTemplateProvider, WikiTemplateProvider>();
  services.AddSingleton<IWikiPageGenerator, WikiPageGenerator>();
  services.AddSingleton<IWikiIngestionGraphUpdater, WikiIngestionGraphUpdater>();
  ```

### 7. Testing
- **Unit**: verify template matching, rendering of placeholders, fallback behavior.
- **Integration**: upload a sample RFP, assert a wiki page appears (`GET /wiki/{slug}`), verify graph node/edge existence, and confirm the regeneration job runs.
- **Coverage**: maintain overall line coverage ≥ 99 % (per Coverage Notes).

## Validation

- **Scenario**: Upload a document `RFP_2026.pdf` that includes `template: RFP`.
- **Expected**: A wiki page `RFP‑2026` appears within the Activity panel stage “Generate Wiki Page (RFP)”; the page is accessible via `/wiki/rfp-2026`.
- **Graph Check**: Neo4j contains a `WikiPage` node linked to the source node via `found_in`.
- **Regeneration**: After the background job finishes, the page includes entity sections, citations, and related‑page links.
- **Telemetry**: Activity panel shows both “Generate Wiki Page” and “Regenerate Wiki Page” stages with timestamps and heartbeat updates.

## Exit Criteria

- ✅ Build succeeds (`dotnet build Aletheia.slnx`).
- ✅ All unit and integration tests pass; coverage ≥ 99 %.
- ✅ Ingestion of a document creates a wiki page automatically, visible via the UI.
- ✅ Graph node/edge creation verified through a test against the in‑memory `IGraphProvider`.
- ✅ Regeneration job enriches the page without errors.
- ✅ Activity panel reflects the new stages.
- ✅ Documentation updated and reviewed.

## Out of Scope

- Persistent job storage beyond the existing in‑memory queue.
- UI redesign of the Wiki navigation (only existing `/wiki` page is used).
- Adding new database schema beyond the existing `WikiPage` table.
- Changing the WRAGS orchestration playbook beyond what is required for template rendering.

