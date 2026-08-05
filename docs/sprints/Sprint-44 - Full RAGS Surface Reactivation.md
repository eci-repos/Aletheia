# Sprint 44 - Full RAGS Surface Reactivation

**Status:** Completed.
**Date:** 2026-08-01.

## Objective

Make Semantic RAGS, WRAGS, GraphRAG, and LazyGraphRAG visible and active across the normal operator workflow, with fallbacks that keep small-corpus and document-scoped prompts responsive.

## Scope

* Search Center exposes all four modes and calls the matching API/client methods.
* WRAGS Wiki exposes all four modes and uses the existing wiki retrieval backend support.
* Copilot no longer treats GraphRAG, LazyGraphRAG, or global graph tools as hidden.
* Broad/global Copilot retrieval tries GraphRAG first, LazyGraphRAG second, and Semantic RAGS last.
* RFP/CMP/feature/document-scoped prompts continue to use Semantic RAGS source evidence.

## Implementation Notes

* Search Center now supports GraphRAG/LazyGraphRAG retrieval and queued ingestion.
* Wiki mode selection now includes GraphRAG and LazyGraphRAG.
* The chat planner routes broad non-RFP corpus requests to `AletheiaKnowledgePlugin.SearchGraphRag`.
* Explicit lazy graph prompts route to `AletheiaKnowledgePlugin.SearchLazyGraphRag`.
* `ChatExecutionEngine` removed hidden graph-tool normalization and reactivated GraphRAG/LazyGraphRAG global search participation with Semantic RAGS fallback.
* Source-scoped fanout remains restricted to `SearchRags` because graph tools do not source-filter reliably.

## Validation

* `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --filter "FullyQualifiedName~ChatPlanningServiceTests|FullyQualifiedName~ChatExecutionEngineTests" --no-restore` passed with 80 tests.
* `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj --no-restore` passed with 32 tests.
* `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --no-restore` passed with 193 tests.
* `dotnet build Aletheia.slnx --no-restore` passed with the existing AngleSharp NU1902 warning.
* `docker compose up -d --build api web` rebuilt/restarted the API and Web containers.
* `GET /health/live`, `GET /health/ready`, `GET http://localhost:8081/search`, and `GET http://localhost:8081/wiki` returned `200`.
