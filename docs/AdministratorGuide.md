# Administrator Guide

## Getting Started

After deploying Aletheia using Docker Compose, access the system at:

- **Web UI using Docker Compose**: http://localhost:8081
- **API Base URL**: http://localhost:8080/api
- **MinIO Console**: http://localhost:9001
- **Neo4j Browser**: http://localhost:7474

## Initial Setup

### 0. Confirm Local Admin Access

In local Docker Compose deployments, the API seeds an `admin` user if one does not already exist. Set `ALETHEIA_ADMIN_PASSWORD` in `.env` before startup. If it is not set and the API runs in Development, the development fallback is `Admin123!`.

If Taxonomy or Repository metadata shows a term such as `RFP` but Copilot/RAGS returns no chunks, run a scoped RAGS index repair:

```http
POST /api/jobs/rags/repair?query=RFP
```

Use `POST /api/jobs/rags/repair` to rebuild all registered Repository documents. The repair runs as a background job and appears in Activity as `RagsRepair`.

Taxonomy and Ontology canonicalize common acronyms. `RFP`, `Rfp`, and legacy `Rpf` should resolve to the displayed concept `RFP`. After a fresh upload, selecting `RFP` in Ontology should show `found_in` relationships to each matching source document.

Production deployments must provide a non-default password through environment variables or a secrets manager before the API starts.

### 1. Create the MinIO Bucket

The system requires a bucket for file storage. Log into the MinIO Console (`http://localhost:9001`) with the credentials from your environment variables and create a bucket named `aletheia-documents`.

### 2. Verify PostgreSQL Extensions

Connect to PostgreSQL and ensure the `pgvector` extension is installed:

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

### 3. Apply Migrations

The API project uses EF Core migrations. Run them on first deployment:

```bash
docker compose exec aletheia-api dotnet ef database update
```

Or run locally:

```bash
cd src/Repository.API
dotnet ef database update
```

## User Management

> **Note**: Aletheia does not include a built-in identity provider. In production, integrate with your organization's IdP (Azure AD, Keycloak, Okta, etc.) and configure ASP.NET Core authentication middleware in `Program.cs`.

### Role-Based Access

The system uses the following conceptual roles:

| Role        | Permissions                                           |
|-------------|-------------------------------------------------------|
| Admin       | Full system access, user management, governance rules |
| Contributor | Upload, edit metadata, start RAG sessions             |
| Viewer      | Search, download, view graph and RAG results          |

## Configuration Reference

### `appsettings.Production.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=postgres;Port=5432;Database=aletheia;Username=aletheia;Password=<password>",
    "Neo4jConnection": "bolt://neo4j:7687",
    "MinIO": "http://minio:9000"
  },
  "MinIO": {
    "AccessKey": "<minio-user>",
    "SecretKey": "<minio-password>",
    "BucketName": "aletheia-documents"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

### Feature Flags

| Flag | Default | Meaning |
| --- | --- | --- |
| FeatureFlags:ShowInternalSearch | false | When false, end users see only the user-facing **Wiki** surface (document briefs) and semantic search. Raw Wiki/WRAGS mode controls, GraphRAG, LazyGraphRAG, and global-graph search are hidden in the UI and return HTTP 404 from the corresponding API endpoints. Set to true for admin/diagnostics work (e.g., in appsettings.Development.json or the production override). |

Example:

`json
{
  "FeatureFlags": {
    "ShowInternalSearch": false
  }
}
`

Environment variable override:

`ash
FeatureFlags__ShowInternalSearch=true
`

### Environment Variable Overrides

All JSON settings can be overridden via environment variables using double-underscore notation:

```bash
ConnectionStrings__DefaultConnection=Host=prod-db;...
MinIO__AccessKey=newkey
```

## API Endpoints Overview

| Controller         | Base Route              | Purpose                              |
|--------------------|-------------------------|--------------------------------------|
| FilesController    | `/api/files`            | Upload, download, delete documents; `/api/files/duplicates` (admin) lists duplicate uploads |
| VersionsController | `/api/versions`         | Document version history             |
| MetadataController | `/api/metadata`         | Document metadata CRUD               |
| SearchController   | `/api/search`           | Full-text and vector search          |
| CollaborationController | `/api/collaboration` | Shared workspaces, annotations       |
| GovernanceController | `/api/governance`     | Policies, retention, compliance      |
| KnowledgeGraphController | `/api/graph`      | Graph nodes, edges, paths            |
| RagsController     | `/api/rags`             | RAG ingestion and retrieval          |
| RagsController     | \/api/rags/status\     | Retrieval diagnostics: chunk/source counts, template-gate skips, recent upload jobs |
| CopilotController  | `/api/copilot`          | AI chat and copilot sessions         |
| GraphRagController | `/api/graphrag`         | GraphRAG search                      |
| LazyGraphRagController | `/api/lazygraphrag` | LazyGraphRAG search                  |
| OntologyController | `/api/ontology`         | Ontology management                  |
| TaxonomyController | `/api/taxonomy`         | Taxonomy management                  |

## RAGS v2 Administration

### Wiki (End Users) and WRAGS (Internal)

The user-facing surface is labeled **Wiki** (never "WRAGS"). Open `http://localhost:8081/wiki` or use the Wiki navigation item.

For end users (default `FeatureFlags:ShowInternalSearch=false`), the Wiki shows **document briefs**: per-document, plain-language summaries that open with the document's stated nature/purpose and then follow the canonical template's ordered sections, grounded and cited, with no chunk/community/graph jargon. Briefs are stored as `wiki_pages` rows with `generated_from = 'document-brief'`. Community summaries and other internal provenance are excluded from Wiki search/recent output.

Briefs are generated automatically after a registered document is ingested (after `EnsureIngestedAsync` succeeds, and after upload ingestion jobs) and can be regenerated on demand:

```http
POST /api/wiki/briefs/regenerate
```

The request body is optional. Omit it (or send `{}`) to regenerate briefs for all registered documents; send `{ "sourceId": "...", "sourceName": "..." }` to regenerate a single document's brief. The call returns a background job snapshot (`IngestionJobSnapshot`, kind `DocumentBriefs`).

Admin/diagnostics mode (`FeatureFlags:ShowInternalSearch=true`) re-enables the internal WRAGS controls on the Wiki page: retrieval mode buttons (Wiki/WRAGS, Semantic, GraphRAG, LazyGraphRAG), expansion, rank/score/strategy metadata, the source chunk index, **Queue regen**, and a **Regenerate briefs** button that queues document-brief regeneration for all registered documents (`POST /api/wiki/briefs/regenerate`). WRAGS stores generated and edited wiki pages in PostgreSQL and exposes them through `/api/wiki`; the supported operator-facing modes are WRAGS, Semantic/Vector RAG, GraphRAG, and LazyGraphRAG. When the flag is off, `/api/wiki/regenerate` and `/api/wiki/regenerate/job` return 404, and `/api/wiki/search` and `/api/wiki/retrieve` reject the `graphrag`/`lazygraphrag` modes with 404.

Use Edit for manual page body changes and Reviewed, Approve, Needs review, and Stale to manage the lifecycle. Reviewed/Approved pages persist `reviewed_by` and `reviewed_at`; stale and needs-review pages surface a warning in the UI. Pages are also flagged stale when linked Repository source metadata is newer than the wiki page update time.

### Search Center Modes

Search Center is the primary human-facing retrieval workbench. Semantic search is the default and remains the primary user path.

When FeatureFlags:ShowInternalSearch=false (default), only **Semantic** mode is visible and ingestion always uses the Semantic RAGS path. The internal WRAGS/GraphRAG/LazyGraphRAG mode buttons are hidden; the corresponding retrieval endpoints (/api/graphrag/retrieve, /api/graphrag/global, /api/lazygraphrag/retrieve, /api/lazygraphrag/global, /api/graph/query/*) return HTTP 404.

When FeatureFlags:ShowInternalSearch=true, the four retrieval modes are visible:

| Mode | Operational behavior |
| --- | --- |
| Semantic | Uses standard RAGS chunks and embeddings for fast passage retrieval |
| WRAGS | Uses durable wiki pages as retrieval context |
| GraphRAG | Uses graph summaries and bounded lazy enrichment when graph context exists |
| LazyGraphRAG | Uses budgeted query-time graph traversal and pruning |

For scoped RFP/CMP/document feature prompts, Copilot still prefers source-scoped Semantic RAGS because that path returns document evidence. Broad corpus prompts can use GraphRAG first, LazyGraphRAG second, and Semantic RAGS fallback.

Search Center direct ingestion uses background jobs for the visible RAG/WRAGS modes. The page should show "Ingestion job queued" quickly, then the Activity panel should show stage, heartbeat, progress, and any failure detail. Search failures should render the technical API error on the page instead of only saying that the search failed.

LazyGraphRAG traversal budgets are guardrails. If optional enrichment reaches a configured LLM/node/relationship/token limit, retrieval should stop expanding and return the best available results; it should not fail merely because the limit was reached.

### Copilot Activity Observability

Copilot chat jobs appear in the global Activity panel. For troubleshooting, Activity shows the prompt snippet, execution approval, queued/running job state, repository tool dispatch, graph fallback when applicable, context verification, and synthesis handoff.

Use these entries before changing timeouts or database credentials. If Activity does not show repository tool dispatch or context verification, investigate chat-agent orchestration first.

### GraphRAG Smoke Test

After deployment:

1. Open `http://localhost:8081`.
2. Sign in with the seeded admin account.
3. Open Search Center.
4. Ingest a short test note in Semantic mode and confirm a job is queued.
5. Search for the topic from that note in Semantic mode.
6. Repeat in WRAGS mode and confirm saved wiki pages can be used as retrieval context.
7. Confirm results include rank, score, retrieval strategy, citations, and source/chunk details.

Recent local Docker validation confirmed this flow with:

- Authenticated login through the Web UI.
- Background ingest returning a job through the RAGS/Search Center path.
- `GET /api/rags/retrieve`, `GET /api/graphrag/retrieve`, and `GET /api/lazygraphrag/retrieve` returning results from Search Center.
- `GET /api/graphrag/retrieve` returning summary-based results.
- `GET /api/lazygraphrag/retrieve` returning budgeted lazy expansion results without traversal-budget false failures.
- `/api/wiki/pages/{id}/status`, `/api/wiki/pages/{id}`, `/api/wiki/pages/{id}/history`, `/api/wiki/pages/{id}/related`, and `/api/wiki/regenerate/job` validating WRAGS lifecycle, edit/history, related-page, and background regeneration behavior.
- No browser console errors.

Upload and queued GraphRAG ingestion now use a faster searchable-first path: chunks and embeddings are created, lightweight source/chunk graph seed nodes are stored, and expensive graph enrichment is deferred to relevant query-time chunks. Legacy/full GraphRAG summary generation can still be slow; use the Activity panel or `GET /api/jobs` to confirm the job is alive, see the current stage, and inspect failures.

Taxonomy/Ontology explorers start with lightweight upload metadata and become richer as GraphRAG queries lazily enrich relevant chunks. Query-time discoveries are synced back to PostgreSQL through `ILazyEnrichmentKnowledgeSink`, so entities, tags, and relationships should appear after related GraphRAG searches or Copilot retrieval touch the source content.

Copilot answers display completion stats under each assistant response: elapsed seconds, estimated tokens per second, estimated completion tokens, retrieved context count, citation count, and a heuristic confidence percentage. For plan-based background executions, the Copilot page shows a progress panel with a status badge, progress bar, step checklist, heartbeats, elapsed time, partial and final results, and an execution telemetry card comparing actuals to the plan estimates. The confidence value is based on retrieval evidence and citations; it is not a calibrated correctness score.

Mandatory Copilot repository tool calls now emit `Tool call` heartbeats while retrieval runs. They use the normal heartbeat cadence, 30 seconds by default, so the watchdog sees active retrieval progress, and they emit an immediate heartbeat when the long operation starts. They also use `ChatExecutionEngine:MandatoryToolTimeoutSeconds` (default 1800 seconds) instead of the generic 30-second step timeout, so broad opportunity/RFP questions have enough time to collect internal evidence. The watchdog is a longer safety net (`HeartbeatWatchdogMissedThreshold = 20`, 10 minutes with the default 30-second cadence). If a retrieval dependency hangs, the **Call repository tool** step should fail with the configured tool timeout or watchdog backstop instead of a short 90-second stall. Use **New chat** to clear the visible browser-local conversation, draft, execution panel, progress, telemetry, and stored Copilot state before testing a fresh prompt; cancel the active job separately if server-side work should stop.

## Health Checks

The following endpoints can be used for load balancer health probes:

```
GET /health/live   -> Liveness probe
GET /health/ready  -> Readiness probe (includes PostgreSQL, Neo4j, and MinIO connectivity)
```

## Upgrading

1. Back up PostgreSQL, Neo4j, and MinIO.
2. Pull the latest images: `docker compose pull`
3. Restart services: `docker compose up -d`
4. Apply any new EF Core migrations: `dotnet ef database update`
5. Verify functionality via the Web UI and API health endpoints.

## Troubleshooting

| Issue                          | Investigation Steps                                |
|--------------------------------|----------------------------------------------------|
| Web UI blank / 502             | Check `aletheia-web` logs; verify Blazor circuit   |
| Upload fails                   | Verify MinIO bucket exists and credentials are valid |
| Search returns no results      | Check `pgvector` extension; verify ingestion ran   |
| Graph queries timeout          | Check Neo4j memory config; add indexes if missing  |
| GraphRAG ingest stays busy     | Check the Activity panel, `GET /api/jobs`, and API logs; upload jobs should be seed-only, while legacy/full summary generation can still be long-running |
| Copilot plan stays in progress | Verify the plan was approved, check `GET /api/copilot/plans/{planId}/progress`, and confirm the API background worker is running |
| Copilot telemetry missing      | Telemetry is recorded only after successful completion; if a job failed or was cancelled, telemetry is unavailable |
| API readiness fails in Docker  | Check PostgreSQL, Neo4j, MinIO health and ensure API image includes `libgssapi-krb5-2` |
| Neo4j runtime method errors    | Verify graph packages use aligned `Neo4j.Driver` versions |
| High memory usage              | Review vector index size; adjust pgvector dimensions |


## Documents: Duplicates and Updates (Sprint 56)

### Duplicate uploads are trapped
Every upload is fingerprinted server-side (SHA-256). If the exact same file is already stored, the API returns **HTTP 409 Conflict** with a structured payload (`duplicate`, `noChange`, `message`, `existingFileId`, `existingFileName`, `existingUploadedAt`, `existingVersion`) and **stores/ingests nothing**. In the Web UI, the file shows a "Duplicate - already exists" badge, an Activity warning is logged, and no ingestion job is queued.

### Updating an existing document
To replace a document's content while keeping its identity:

1. Open **Browse** and click the update (↻) action on the document, or open `Upload` with `?update=<fileId>&fileName=<name>`.
2. Select the new file and click **Upload New Version**.
3. The old state is snapshotted as a named version (visible via the versions API), the blob and current metadata are replaced, and a background ingestion job re-ingests the same source - replacing embeddings, knowledge-index rows, and graph nodes - then regenerates the Wiki brief.

If the submitted file is byte-identical to the current version, the upload is trapped with "Already current - no changes" and no new version is created.

### Versioning limitation
Versioning is metadata-level: `GET /api/versions` lists prior versions, but all versions of a document share the single blob stored under `fileId/fileName` in MinIO. Downloading an older version returns the current blob. Blob-level (content-addressed) versioning is a future enhancement.

### Finding and removing duplicates that already exist (admin)
`GET /api/files/duplicates` (requires the Administrator role) returns every `file_metadata` row whose content hash is shared by more than one row. Review the list, then delete the duplicate artifacts via the existing DELETE flow (Browse or `DELETE /api/files?fileId=...&fileName=...`), which also removes vectors, knowledge-index rows, and metadata. No automatic deletion is performed.

### Applying the content-hash schema change
Fresh deployments receive `content_hash` from `init.sql`. For existing deployments, run once (idempotent):

```sql
ALTER TABLE file_metadata ADD COLUMN IF NOT EXISTS content_hash TEXT;
CREATE INDEX IF NOT EXISTS idx_file_metadata_content_hash ON file_metadata(content_hash);
```

The same statements are provided in `src/Repository.Infrastructure.PostgreSQL/Migrations/2026-08-05-file-metadata-content-hash.sql`. Rows uploaded before this change have a NULL `content_hash`; they are re-fingerprinted on their next upload/update.

### Retrieval Options (Sprint 57)

| Setting | Default | Meaning |
| --- | --- | --- |
| RAGS:MinimumScore | 0 | Minimum cosine similarity (0..1) for vector results. When the best vector result is below this floor (or the vector search returns nothing), retrieval falls back to keyword search over chunk content and file names (`RetrievalStrategy` = `keyword`). 0 keeps the classic behavior (fallback only when vector results are empty); raise it (e.g., 0.35) to make weak vector matches fall back to lexical results. |

Environment variable override:

```bash
RAGS__MinimumScore=0.35
```

### Embedding Provider and Re-embedding (Sprint 57)

| Setting | Default | Meaning |
| --- | --- | --- |
| AI:EmbeddingProvider | Simple | "Simple" = deterministic 128-dim hash (no AI required, lexical matching). "Ollama" = real embeddings via the enabled Ollama provider's EmbeddingModel. Falls back to Simple when Ollama is requested but no enabled Ollama provider has an EmbeddingModel. |
| AI:EmbeddingDimension | 768 | Expected embedding dimension for the embeddings table schema (nomic-embed-text = 768). Set to match the chosen model; the provider updates to the actual dimension after the first call. |
| AI:Providers[*].EmbeddingModel | - | Model used for embeddings (e.g., nomic-embed-text on the LocalOllama provider). |

Environment variable overrides:

```bash
AI__EmbeddingProvider=Ollama
AI__EmbeddingDimension=768
```

**Switching to Ollama embeddings:** set `AI:EmbeddingProvider=Ollama` and `EmbeddingModel` on the provider, restart the API (the `PgVectorSchemaInitializer` migrates the `embeddings.embedding` column to the new dimension automatically, dropping/recreating the vector index), then re-embed:

```http
POST /api/jobs/rags/reembed
```

The Reembed job (kind `ReembedIngestion`) re-runs ingestion for every registered document, replacing embeddings, knowledge-index rows, and graph nodes, then regenerates Wiki briefs. Track it in the Activity panel or `GET /api/jobs`. The Search Center admin section (with `FeatureFlags:ShowInternalSearch=true`) has a **Re-embed all documents** button.

### Knowledge Themes (Sprint 58)

- **Concept**: every canonical template in `docs/doc-templates` declares a knowledge theme on its first line (`Theme: <Theme>`, e.g. `Theme: Analysis`). The theme is persisted on `file_metadata.template_name` / `file_metadata.theme` at ingestion (idempotent migration `2026-08-06-file-metadata-template-theme.sql`; fresh installs get it from `init.sql`). Documents ingested before Sprint 58 fall back to a theme derived from the file name via the template registry.
- **Templates**: a template with no `Theme:` line resolves to `Uncategorized`. New document kinds require a template AND a theme before documents of that kind can be ingested and theme-filtered.
- **End users**: the Copilot session picks knowledge themes at session creation (theme picker) and shows them as chips in the session header, editable mid-session. An empty selection ("All themes") means all documents - the pre-Sprint-58 behavior.
- **API**: `GET /api/knowledge/themes` (authenticated) returns `[{ theme, documentCount }]`, including themes with zero registered documents so the picker is stable.
- **Enforcement**: the selected themes are resolved to registered source ids (`IKnowledgeThemeService`, singleton) and enforced in every RAGS retrieval path - vector and keyword fallback (`RetrievalRequest.SourceIds`, `source_id = ANY(...)` in PgVectorStore) - and on repository-tool results in the execution engine. A named document outside the session themes returns no results from that document.