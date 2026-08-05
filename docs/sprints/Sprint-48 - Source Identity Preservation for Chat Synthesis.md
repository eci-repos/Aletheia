# Sprint 48 - Source Identity Preservation for Chat Synthesis

**Status:** Completed.
**Date:** 2026-08-01.

## Objective

Prevent Copilot from blending project details across distinct repository documents, especially CMP 2022 and CMP 2026 RFP analysis documents.

## Problem

The graph can show sources correctly as separate entities, but chat synthesis receives mixed retrieval context and can attribute CMP 2022 details to CMP 2026 or omit one source entirely. The fix must preserve source identity through retrieval handoff, prompt context, synthesis instructions, and progress/audit telemetry.

## Scope

* Partition retrieved context by `SourceId`.
* Build one clearly bounded source block per document.
* Include source name and source ID in every block.
* Prohibit cross-source fact reuse.
* Require one answer section per source for multi-source prompts.
* Keep named-document prompts restricted to the named source block.
* Repair the progress-step issue described in `docs/issues/issue-01 - handout.md`.

## Implementation Notes

* `RetrievalAugmentedPromptBuilder` now groups results by source before writing retrieved context.
* The prompt explicitly states the number of distinct source documents and forbids blending or omitting source sections.
* `ChatExecutionEngine` now marks `Synthesis` and `Synthesizing answer` completed on successful jobs.
* The post-synthesis check is a soft warning based on source-name coverage, not a hidden-tag check.

## Validation

* `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --filter "FullyQualifiedName~RetrievalAugmentedPromptBuilder_partitions_context_by_source_identity|FullyQualifiedName~Engine_marks_all_successful_steps_completed" --no-restore` passed with 2 tests.
* Focused repair regression run passed with 4 tests: source partitioning, successful-step completion, GraphRAG fallback, and step timeout.
* `dotnet test tests/RAGS.UnitTests/RAGS.UnitTests.csproj --no-restore` passed with 194 tests.
* `dotnet test tests/Repository.IntegrationTests/Repository.IntegrationTests.csproj --no-restore` passed with 8 tests.
* `dotnet test tests/Aletheia.Web.UnitTests/Aletheia.Web.UnitTests.csproj --no-restore` passed with 32 tests.
* `dotnet build Aletheia.slnx --no-restore` passed with the existing AngleSharp NU1902 warning.
* `docker compose up -d --build api web` rebuilt/restarted the API and Web containers after fixing the API Dockerfile publish path.
* `GET /health/live`, `GET /health/ready`, and `GET http://localhost:8081/copilot` returned `200`.
