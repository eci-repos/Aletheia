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

- Every ingested document must match a canonical template in `docs/doc-templates` (the file name carries the clue, e.g. `CMP 2026 - 3. RFP Analysis.docx` -> `3.0 - RFP Analysis`).
- Every template file must declare its knowledge theme on the **first line**: `Theme: <Theme>` (e.g. `Theme: Analysis`). Missing or unknown themes resolve to `Uncategorized`.
- Themes drive the session-level knowledge filter (Sprint 58): the end-user picks themes at session creation and Copilot retrieves only from documents whose template theme is selected. New document kinds therefore require both a template and a theme before documents of that kind can be ingested and theme-filtered.