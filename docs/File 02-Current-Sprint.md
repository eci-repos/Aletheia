# Sprint 56 - Duplicate Upload Detection and Document Update Flow

**Status:** Active

Full authority: `docs/sprints/Sprint-56 - Duplicate Upload Detection and Document Update Flow.md` (created 2026-08-05). This file is the active implementation authority; the referenced sprint file defines the authorized scope.

Sprint 55 (Document Briefs and the End-User Wiki) is **complete and committed** (HEAD `8e4bcb4`); see `docs/sprints/Sprint-55 - Document Briefs and End-User Wiki.md` and `docs/sprints/Sprint-55 - Session Handoff 2026-08-04.md`. Remaining Sprint 55 verification (GitHub Actions CI run, Docker smoke test) is carried in the Sprint 56 handoff notes as verification work, not new feature work.

## Objective

1. **Trap duplicate uploads** of the exact same file: notify the user, store/ingest nothing a second time.
2. **Explicit document update path**: submit a changed file against an existing document, keep its identity (fileId), create a new version, replace old content in search/graph/Wiki, regenerate the Wiki brief, and list prior versions.

## Authorized Work (summary - see sprint file for details)

1. **Content fingerprinting** - server-side SHA-256 of uploaded bytes; `ContentHash` on `FileMetadata`; `content_hash` column + index in `init.sql` plus an idempotent migration for existing deployments.
2. **Duplicate trap** - `IMetadataRepository.FindByContentHashAsync` (or `IDuplicateDetectionService`); on match, block storage/metadata/ingestion and return HTTP 409 with `{ duplicate, message, existingFileId, existingFileName, existingUploadedAt, existingVersion }`; Web Upload page shows "Duplicate - already exists", Activity warning, no ingestion tracking.
3. **Document update flow** - optional `existingFileId` on `POST /api/files/upload`; same-hash update trapped; else new version under same fileId, new blob + metadata + content_hash, ingestion job with the same sourceId, prior knowledge/graph rows replaced (reuse `DeleteSourceAsync` paths), brief regenerated; Web "Update existing document" / "Upload new version" UI; `RepositoryApiClient.UpdateDocumentAsync`.
4. **Tests** - hash persistence; duplicate blocks storage/ingestion; update creates version with same sourceId; identical-content update trapped; 409/400 shapes; Web mapping; existing suites green.
5. **Existing duplicate cleanup** - admin-gated duplicate report (content_hash matches) + documented manual removal using the existing DELETE flow; no automatic deletion.
6. **Docs** - Architecture, Administrator/Operations guides, AGENTS/handoff notes.

## Acceptance Criteria

- Same-file re-post: trapped, user notified, zero new artifacts (blob, metadata, job, chunks, graph, brief).
- Update: new version under same fileId, old content replaced in search/graph/Wiki, version history lists prior versions, brief regenerated.
- No-change update: notified, nothing stored, no new version.
- RAGS / Foundation / Repository suites green; Web C#/Razor compiles.

## Out of Scope

- Cross-tenant hash matching; blob dedup across unrelated fileIds; graph algorithm improvements; server-side multi-tenant wiki history.

---

## Progress (2026-08-05)

- Sprint 56 sprint file created (`docs/sprints/Sprint-56 - Duplicate Upload Detection and Document Update Flow.md`).
- **Content fingerprinting**: SHA-256 computed server-side in `FilesController.Upload`; `FileMetadata.ContentHash` + `UploadRequest.ContentHash`; `file_metadata.content_hash` column + `idx_file_metadata_content_hash` in `init.sql` and idempotent migration `src/Repository.Infrastructure.PostgreSQL/Migrations/2026-08-05-file-metadata-content-hash.sql`; `PostgreSqlMetadataRepository` persists/loads the hash.
- **Duplicate trap**: `IDuplicateDetectionService`/`DuplicateDetectionService` (Repository.Application, singleton) + `IMetadataRepository.FindByContentHashAsync` (default no-op on the interface; PostgreSQL override). Exact duplicates return HTTP 409 with the structured payload; Web Upload page shows "Duplicate - already exists" / "Already current - no changes" badges, Activity warnings, and skips ingestion tracking.
- **Document update flow**: `POST /api/files/upload` optional `existingFileId` -> no-change 409, 400 for missing document, version snapshot via `IVersioningUseCase`, blob + unversioned metadata row replaced (same fileId), ingestion job with the same sourceId; replace semantics in `EnsureIngestedAsync` (knowledge-index + graph `DeleteSourceAsync` before RAGS ingest; brief regenerates). Web: Browse update (↻) action + Upload update mode.
- **Admin cleanup report**: `GET /api/files/duplicates` (`[Authorize(Roles = Administrator)]`) lists rows sharing a content hash; manual DELETE flow documented; no automatic deletion.
- **Tests**: Repository.UnitTests 102 passed (new DuplicateDetectionServiceTests + FilesControllerTests: 409 shapes, no-change, update versioning, ingestion enqueue, 400s). RAGS 225 and Foundation 55 still green. `RepositoryApiClientUploadTests` (Web) added; Web.UnitTests not runnable in this sandbox (pre-existing WASM task-host failure; CI does not run Web.UnitTests).
- **Docs**: Architecture, AdministratorGuide, OperationsGuide, AGENTS.md, File 03-openhands.md updated.



## Next Sprint Prepared

- `docs/sprints/Sprint-57 - Search Center Retrieval Quality and Troubleshooting.md` (Status: Planned) is the prepared next sprint: Search Center diagnostics (empty-corpus messaging, `GET /api/rags/status`) + retrieval quality (configurable real embeddings, score floor, keyword fallback, Reembed job). It becomes active only after Sprint 56 is committed and verified.
- `docs/OperationsGuide.md` already gained the "Search Center Troubleshooting (Sprint 56/57)" section (empty-embeddings diagnosis, verification SQL, example queries).
