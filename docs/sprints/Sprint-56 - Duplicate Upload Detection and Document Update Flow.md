# Sprint 56 - Duplicate Upload Detection and Document Update Flow

**Status:** Active

## Objective

1. **Trap duplicate uploads.** When a user posts the exact same file that already exists in the repository, do NOT store it, do NOT re-ingest it, and do NOT create a second Wiki brief. Instead, detect the duplicate and notify the user (existing file name, upload date, and a clear "nothing was uploaded" message).
2. **Provide an explicit document update path.** When a user needs to submit an update of a document that already exists (same logical document, new content), support it explicitly: keep the document identity (fileId), create a new version, replace the old content in search/graph/Wiki, and list prior versions.

## Background

- Confirmed in field testing (2026-08-05): posting the same file twice creates a duplicate artifact. The Web Upload page generates `Guid.NewGuid()` per file (`src/Aletheia.Web/Pages/Upload.razor` -> `RepositoryApiClient.UploadAsync` -> `POST /api/files/upload`), and `FilesController.Upload` (`src/Repository.API/Controllers/FilesController.cs`) writes the blob + metadata and enqueues an ingestion job with no duplicate check.
- `file_metadata` has no content fingerprint column. Schema lives in `init.sql` (docker) with unique constraint `(file_id, COALESCE(version,''))`; `PostgreSqlMetadataRepository.SaveAsync` silently upserts on that key, so reusing a fileId overwrites metadata instead of signaling anything.
- Versioning exists but is incomplete for this purpose: `IVersioningService`/`PostgreSqlVersioningService` only copy a metadata row (no blob versioning, no ingestion), and `VersionsController` is not wired into the upload flow or the Web UI.
- Re-ingestion is already content-replacing for embeddings: `RagsService.IngestAsync` deletes existing embeddings for the source, then re-chunks/re-embeds. Knowledge-index rows (`UploadedContentKnowledgeIndexer.DeleteSourceAsync`) and graph seed/chunk/entity nodes need the same replace treatment in the update flow.
- Wiki document briefs regenerate automatically after ingestion (Sprint 55, `RepositoryKnowledgeSourceIngestionService` -> `EnqueueDocumentBriefs`); the update flow must preserve this so the brief reflects the new content. Wiki source-change stale detection already exists as a synergy.
- `UploadedContentKnowledgeIndexer` already uses SHA-256 (`StableId`) internally, so adding a server-side content hash fits existing patterns.

## Deliverables

### 1. Content fingerprinting (server-side)
- Compute **SHA-256 over the uploaded bytes** before any storage write. `FilesController.Upload` already writes the multipart stream to a temp file (`CopyToTemporaryFileAsync`); hash that temp file and pass the hash into the upload path.
- Add `ContentHash` to `FileMetadata` (`src/Repository.Abstractions/Models/FileMetadata.cs`) and surface it in `UploadResponse`.
- Schema: add `content_hash TEXT` to `file_metadata` in `init.sql` + index `idx_file_metadata_content_hash`. For existing deployments, add an idempotent migration following the repo pattern (`ALTER TABLE file_metadata ADD COLUMN IF NOT EXISTS content_hash TEXT; CREATE INDEX IF NOT EXISTS ...`), e.g., a schema initializer/migration under `src/Repository.Infrastructure.PostgreSQL` (mirror the RAGS `PostgreSqlWikiSchemaInitializer`/`Migrations` pattern).

### 2. Duplicate trap (API + Web)
- Add `IMetadataRepository.FindByContentHashAsync(string contentHash, ...)` (or a small `IDuplicateDetectionService` in Repository.Application over `IMetadataRepository`).
- In the upload path: after hashing, look up an existing non-deleted row with the same `content_hash`.
  - **Found** -> do NOT call the storage provider, do NOT save metadata, do NOT enqueue an ingestion job. Return **HTTP 409 Conflict** with a structured payload:
    `{ duplicate = true, message = "This exact file is already in the repository (uploaded {date} as {name}). Nothing was uploaded.", existingFileId, existingFileName, existingUploadedAt, existingVersion }`.
  - Also treat same hash + same fileId as duplicate (idempotent no-op) so retries never double-ingest.
- Web (`Upload.razor` + `RepositoryApiClient`): map the 409 to a **"Duplicate - already exists"** badge per file, an Activity panel warning, and skip `RecentGraphContext.RecordDocumentAsync` and ingestion tracking.

### 3. Document update flow (API + Web)
- Semantics: an update submits a **changed file against an existing document**, preserving `fileId` (document identity), creating a **new version**, and replacing old content in search/graph/Wiki.
- API: `FilesController.Upload` gains optional `[FromForm] Guid? existingFileId`.
  - Resolve the existing latest metadata row (`fileId`, null version).
  - If the new content hash equals the latest version's hash -> 409 duplicate/no-change payload ("no changes; nothing stored, no new version").
  - Otherwise: create a new version label (reuse `IVersioningService.CreateVersionAsync`, or write a new metadata row with a short version id), store the new blob under the same `fileId`, save metadata with the new version + `content_hash`, and enqueue the ingestion job with the **same sourceId**.
  - Replace, don't accumulate: before/within ingestion, clear prior knowledge-index rows and graph nodes for the source (reuse the `DeleteSourceAsync` paths from `UploadedContentKnowledgeIndexer`, `IVectorStore.DeleteBySourceAsync`, and graph source/chunk cleanup) so search, graph seed, and Wiki reflect only the new content. `RagsService.IngestAsync` already replaces embeddings.
  - Preserve the automatic document-brief trigger (same sourceId) so the Wiki brief is regenerated for the updated content.
- Web: add an **"Update existing document"** mode on `Upload.razor` (choose existing document + new file) and/or an **"Upload new version"** action surfaced on `Browse.razor`/`MetadataEditor.razor`. Add `RepositoryApiClient.UpdateDocumentAsync(...)`.
- Keep existing synchronous RAGS/GraphRAG/LazyGraphRAG endpoints and the `/api/jobs` snapshot contract untouched.

### 4. Tests
- Unit (Repository/RAGS as appropriate):
  - Content hash computed and persisted with metadata.
  - Duplicate detection returns the existing metadata row and blocks storage + ingestion + brief queueing.
  - Update path with a changed file creates a new version, keeps the same fileId, and enqueues ingestion with the same sourceId.
  - Update with identical content is trapped (no new version, no ingestion).
- Controller tests: 409 payload shape for duplicates; 400 when `existingFileId` does not exist.
- Web unit tests: client maps 409 -> duplicate result; Upload page renders the duplicate badge and skips tracking.
- Existing suites remain green: RAGS / Foundation / Repository; Web C# compiles (`CoreCompile`).

### 5. Existing duplicate cleanup (for data already created)
- After `content_hash` is populated, provide a small admin diagnostic: a `GET /api/files/duplicates` (admin-gated) or documented SQL query that lists `file_metadata` rows sharing the same `content_hash` (same exact file stored multiple times under different fileIds), including counts and upload dates.
- AdministratorGuide documents how to review and remove duplicate artifacts (existing DELETE flow: vectors, knowledge index, blob, metadata) and confirms Wiki briefs for the removed duplicates are handled (brief rows for deleted sources are removed/ignored).
- No automatic deletion: cleanup is a manual, reviewed admin action.

### 6. Docs
- `docs/Architecture.md`: content fingerprinting, duplicate trap, update/versioning semantics (identity vs content).
- `docs/AdministratorGuide.md` / `docs/OperationsGuide.md`: what users see on a duplicate upload; how to update an existing document; how versions appear in search/Wiki.
- `docs/File 03-openhands.md`, `docs/File 02-Current-Sprint.md`, this sprint file, and the session handoff updated.

## Acceptance Criteria

- Posting the exact same file twice: the second attempt is trapped, the user is notified (existing name + date, "nothing was uploaded"), and no new blob, metadata row, ingestion job, chunk/embedding, graph node, or Wiki brief is created.
- Update path: a changed file submitted against an existing document creates a new version under the same fileId; old content is replaced in search, graph, and Wiki; version history lists prior versions; the document brief is regenerated.
- Update with identical content: user is notified that there are no changes; nothing is stored and no version is created.
- RAGS / Foundation / Repository suites green; Web C#/Razor compiles.

## Out of Scope

- Cross-tenant hash matching (duplicate check is tenant-scoped).
- Blob-level deduplication of unrelated documents that happen to share content but use different fileIds (only exact same-file re-posts and same-fileId updates are trapped).
- Improving graph algorithms or community summaries.
- Server-side multi-tenant wiki history.

---

## Implementation Status (2026-08-05)

Implemented and locally verified:

1. **Content fingerprinting** - SHA-256 over the uploaded temp file in `FilesController.Upload`; `FileMetadata.ContentHash` / `UploadRequest.ContentHash`; `content_hash` column + `idx_file_metadata_content_hash` in `init.sql` and idempotent migration `src/Repository.Infrastructure.PostgreSQL/Migrations/2026-08-05-file-metadata-content-hash.sql`; `PostgreSqlMetadataRepository` persists/loads the hash (`SaveAsync`, `GetAsync`, `SearchAsync`, `FindByContentHashAsync`, `ListContentHashDuplicatesAsync`).
2. **Duplicate trap** - `IDuplicateDetectionService`/`DuplicateDetectionService` (singleton); `IMetadataRepository.FindByContentHashAsync` (default no-op on the interface, PostgreSQL override). New-upload duplicates return HTTP 409 `{ duplicate, noChange, message, existingFileId, existingFileName, existingUploadedAt, existingVersion }`; no blob/metadata/ingestion/brief. Web Upload page shows "Duplicate - already exists" / "Already current - no changes" badges, Activity warning, skips tracking; `RepositoryApiClient.UploadAsync` maps 409.
3. **Document update flow** - `POST /api/files/upload` optional `existingFileId`: missing document -> 400; same hash -> no-change 409; changed file -> `IVersioningUseCase.CreateVersionAsync` snapshot + blob/unversioned-metadata replace + ingestion job with the same sourceId. Replace semantics in `RepositoryKnowledgeSourceIngestionService.EnsureIngestedAsync` (knowledge-index + graph `DeleteSourceAsync` before RAGS ingest; Wiki brief regenerates). `IGraphProvider.DeleteSourceAsync` (default not-supported) with Neo4j DETACH DELETE by `n.sourceId`. Web: Browse update (↻) action + Upload update mode (`?update=<fileId>&fileName=<name>`).
4. **Tests** - Repository.UnitTests 102 passed (DuplicateDetectionServiceTests x4, FilesControllerTests x7); RAGS 225 and Foundation 55 green; Web `RepositoryApiClientUploadTests` x4 added (not runnable in sandbox - pre-existing WASM task-host failure; CI does not run Web.UnitTests).
5. **Existing duplicate cleanup** - `GET /api/files/duplicates` admin report implemented; manual removal documented.
6. **Docs** - Architecture, AdministratorGuide, OperationsGuide, AGENTS.md, File 02, File 03 updated.

Remaining: CI run on GitHub Actions, Docker smoke test (upload -> duplicate trap -> update flow), commit.


