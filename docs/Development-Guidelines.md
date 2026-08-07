# Development Guidelines

## Architecture Standards

- Follow Clean Architecture, Hexagonal Architecture, and DDD.
- Keep dependencies pointing inward.
- Domain and Foundation projects must not reference infrastructure implementations.
- Use abstractions for all external dependencies.

## Build & Test

- Target framework: .NET 10.0
- Build:
  ```powershell
  dotnet build Aletheia.slnx
  ```
- Test:
  ```powershell
  dotnet test tests/Aletheia.Foundation.UnitTests/Aletheia.Foundation.UnitTests.csproj
  ```
- Coverage target: 80% minimum.

## Documentation

- Update README, Architecture, and Roadmap for every completed feature.
- Keep documentation aligned with the current sprint scope.

## Coding Standards

- Keep changes minimal and focused on approved phases.
- Prefer small, testable units.
- Avoid speculative features and future-phase implementations.

## Canonical Templates & Knowledge Themes

- Every document is matched to a canonical template in `docs/doc-templates` when one exists (the file name carries the clue, e.g. `CMP 2026 - 3. RFP Analysis.docx` -> `3.0 - RFP Analysis`). Since Sprint 59 the gate is **softened**: a document with no matching template is still ingested (RAGS + knowledge index + graph seed) with `template_status = Uncategorized`, so a new document kind is never lost. Template-dependent features (document briefs, per-section retrieval, theme) wait until the row is `Canonical`.
- Every template file declares its knowledge theme **set** on the **first line**: `Theme: <Theme>` (e.g. `Theme: Analysis`, or `Theme: Analysis, As-Built` for multiple). Missing or unknown themes resolve to `Uncategorized`. `file_metadata.theme` is a `text[]` set; a document in multiple themes is matched by any and counted in each.
- Themes drive the knowledge filter: the end-user picks themes in Copilot (session-level, Sprint 58) and in Search Center (shared scope on semantic search, Sprint 59). New document kinds still require a template (and themes) for the full experience, but can be ingested as `Uncategorized` first and promoted later via `POST /api/knowledge/reevaluate`.
- When adding a new document kind: write its template under `docs/doc-templates`, upload a document of that kind, then run re-evaluation (Search Center admin panel or the API) to promote existing uncategorized rows and generate their document briefs.