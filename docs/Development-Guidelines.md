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
