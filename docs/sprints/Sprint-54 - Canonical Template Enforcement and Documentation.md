# Sprint 54 - Canonical Template Enforcement and Documentation

**Status:** Active (implementation complete; pending end-to-end verification)

## Objective

Make the canonical document templates (docs/doc-templates) a first-class, enforced contract:

1. Every ingested document must resolve to a canonical template; the document name carries the clue (e.g., `CMP 2026 - 3. RFP Analysis.docx` -> canonical `3.0 - RFP Analysis`).
2. If no canonical template matches a document, **ingestion is stopped** with a clear error.
3. Document the template<->document relationship in the architecture, AGENTS, and handoff docs.

## Background

Sprint 53 added the `DocumentTemplateRegistry` (ordered sections per template, token-overlap matching) and template-guided summaries. This sprint makes the canonical match **mandatory at ingestion time** and records the relationship in the repository documentation so new document kinds are always paired with a template.

## Deliverables

1. **Registry canonical lookup**
   - `IDocumentTemplateRegistry.TryGetCanonicalName(string fileName)` returns the matched template name (or null). `TryGetSections` reuses the same matcher.

2. **Ingestion gate** (`RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync`)
   - Inject `IDocumentTemplateRegistry` (optional parameter; resolved from DI in production).
   - At the start of ingestion, resolve the canonical name from `source.SourceName`; if none is found, log a warning and return failure: "Ingestion stopped: no canonical document template found for '<name>'."
   - This gates upload ingestion jobs, hydration, and plugin-triggered ingestion (all flow through `EnsureIngestedAsync`).

3. **Documentation**
   - `docs/Architecture.md`: new "Canonical Document Templates" section - templates define the canonical format (ordered sections); document names identify their canonical (the clue in the suffix); the registry matches by token overlap; ingestion requires a canonical; summaries follow the template order.
   - `AGENTS.md`: note under Repository Guidance - documents must match a canonical template in docs/doc-templates; new document kinds require a new template; ingestion stops without a match.
   - `docs/File 03-openhands.md`: execution rule - never ingest/register a document without a canonical template; add a template when introducing a new document kind.

## Acceptance Criteria

- `TryGetCanonicalName("CMP 2026 - 3. RFP Analysis.docx") == "3.0 - RFP Analysis"`; unknown names return null.
- `EnsureIngestedAsync` returns failure (and does not download/extract/index) for a source without a canonical template.
- Docs updated in Architecture.md, AGENTS.md, File 03-openhands.md.
- Existing suites remain green.


## Execution Status (2026-08-03)

Implemented and verified:

- `IDocumentTemplateRegistry.TryGetCanonicalName` added (shared matcher with `TryGetSections`).
- `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` now requires a canonical template match: ingestion stops with "Ingestion stopped: no canonical document template found for '<name>'." before download/extract/index. The gate covers upload ingestion jobs, hydration, and plugin-triggered ingestion.
- Docs updated: `docs/Architecture.md` (Canonical Document Templates section), `AGENTS.md` (Repository Guidance note), `docs/File 03-openhands.md` (execution rule).

Tests (all green):

- RAGS.UnitTests: 211/211 (new: `TryGetCanonicalName` x2, `CanonicalTemplateIngestionTests` x2).
- Aletheia.Foundation.UnitTests: 55/55; Repository.UnitTests: 91/91; Aletheia.Web C# compiles.

Operational note: the gate is enforced by default. Documents that do not match a template under `docs/doc-templates` will not be ingested; add a template first when introducing a new document kind.
