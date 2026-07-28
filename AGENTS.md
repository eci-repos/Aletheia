# AGENTS

## Repository Guidance

- Follow documentation order: `docs/File 00-Aletheia-Charter.md`, `docs/File 01-Aletheia-WorkPlan.md`, `docs/File 02-Current-Sprint.md`, `docs/File 03-openhands.md`.
- Current sprint authorizes Phase 21 (RAGS v2 Intelligence and Background Operations) only.

## Build & Test

- Build: `dotnet build Aletheia.slnx`
- Tests: `dotnet test tests/Aletheia.Foundation.UnitTests/Aletheia.Foundation.UnitTests.csproj`
- Repository tests: `dotnet test tests/Repository.UnitTests/Repository.UnitTests.csproj`
- RAGS tests: `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj`

## CI/CD

- GitHub Actions workflow: `.github/workflows/ci.yml` (restore/build/test with coverage collection).

## Coverage Notes

- Use `dotnet test <csproj> --collect:"XPlat Code Coverage"` to generate Cobertura files under `tests/*/TestResults/*/coverage.cobertura.xml`.
- `Aletheia.Foundation.Domain.DomainEvent` line 3 is the type declaration and remains uncovered in coverage (line rate tops out at 99.47%).
- Repository test coverage reports include `Aletheia.Foundation` classes at 0% unless foundation tests are run; filter `Aletheia.Repository*` classes for repository-only coverage.

# GraphRAG Infrastructure Patterns

## Singleton Registration Rule
- `IGraphProvider` is registered as a singleton in DI. Any service that depends on it should also be registered as a singleton to avoid scoped/singleton capture issues.
- This applies to: `GraphSummaryService`, `HierarchicalSummaryService`, `CommunityDetectionService`, `CitationPathService`, `GraphContextBuilder`, `GraphAdminService`.

## GraphNode / GraphEdge Model
- Namespace: `Aletheia.KnowledgeGraph.Abstractions.Models`
- `GraphEdge` property is `RelationshipType`, not `Type`.
- Source nodes use `Type == "Source"`.
- Entity-to-source edges use `RelationshipType == "found_in"`.
- `GraphNode` properties dictionary keys: `"sourceId"`, `"sourceName"`, `"communityId"` for metadata tracking.

## Result<T> Pattern
- From `Aletheia.Foundation.Shared`.
- Use `Result.Success()`, `Result.Failure(string)`, `Result<T>.Success(value)`, `Result<T>.Failure(string)`.
- Check `IsSuccess` / `IsFailure` and `Value` on results before proceeding.

## Controllers
- Follow existing ASP.NET Core pattern in `Repository.API.Controllers`.
- Return `BadRequest(new { error = result.Error })` on failure.
- Use `[Route("api/...")]` attribute routing.

# OpenHands Instructions

Current Sprint is authoritative for active implementation scope.

The active sprint determines what phases are authorized.

Completed phases are considered closed unless explicitly reopened.

If AGENTS.md and Current-Sprint.md disagree:

Current-Sprint.md takes precedence.

Do not request clarification if Current-Sprint.md clearly identifies
the authorized phases.
