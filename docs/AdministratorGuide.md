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

### Environment Variable Overrides

All JSON settings can be overridden via environment variables using double-underscore notation:

```bash
ConnectionStrings__DefaultConnection=Host=prod-db;...
MinIO__AccessKey=newkey
```

## API Endpoints Overview

| Controller         | Base Route              | Purpose                              |
|--------------------|-------------------------|--------------------------------------|
| FilesController    | `/api/files`            | Upload, download, list documents     |
| VersionsController | `/api/versions`         | Document version history             |
| MetadataController | `/api/metadata`         | Document metadata CRUD               |
| SearchController   | `/api/search`           | Full-text and vector search          |
| CollaborationController | `/api/collaboration` | Shared workspaces, annotations       |
| GovernanceController | `/api/governance`     | Policies, retention, compliance      |
| KnowledgeGraphController | `/api/graph`      | Graph nodes, edges, paths            |
| RagsController     | `/api/rags`             | RAG ingestion and retrieval          |
| CopilotController  | `/api/copilot`          | AI chat and copilot sessions         |
| GraphRagController | `/api/graphrag`         | GraphRAG search                      |
| LazyGraphRagController | `/api/lazygraphrag` | LazyGraphRAG search                  |
| OntologyController | `/api/ontology`         | Ontology management                  |
| TaxonomyController | `/api/taxonomy`         | Taxonomy management                  |

## RAGS v2 Administration

### WRAGS Wiki

WRAGS is the LLM Wiki surface for Aletheia. Open `http://localhost:8081/wiki` or use the WRAGS navigation item.

WRAGS stores generated and edited wiki pages in PostgreSQL and exposes them through `/api/wiki`. WRAGS mode searches saved pages first, then generates from GraphRAG on first miss, with LazyGraphRAG and Semantic fallback. Operators can also force one mode when comparing behavior. Results show rank, score, version, lifecycle status, stale warnings, retrieval strategy, citations, related topics, related pages, source ID, chunk index, updated time, and history.

Use Queue regen when source knowledge has changed and a page needs to be refreshed. Regeneration runs as a background job, updates the durable page snapshot, increments the version for matching topic/title/mode, and records the prior revision in history.

Use Edit for manual page body changes and Reviewed, Approve, Needs review, and Stale to manage the lifecycle. Reviewed/Approved pages persist `reviewed_by` and `reviewed_at`; stale and needs-review pages surface a warning in the UI. Pages are also flagged stale when linked Repository source metadata is newer than the wiki page update time.

### Search Center Modes

The Web UI Search Center supports three retrieval modes:

| Mode | Operational behavior |
| --- | --- |
| Semantic | Uses standard RAGS chunks and embeddings for fast passage retrieval |
| WRAGS | Uses durable wiki pages as retrieval context |
| GraphRAG | Uses typed Neo4j entities, relationships, hierarchical communities, stored entity/community summaries, and the page's expansion-hop control |
| LazyGraphRAG | Uses low-cost corpus statistics, query-time candidate discovery, budgeted best-first traversal, pruning, and the page's expansion-limit control |

GraphRAG results may display retrieval strategies such as `summary-entity` or `summary-community`. LazyGraphRAG results may display combined strategies such as `lazy-semantic+lazy-corpus-expansion+lazy-entity-expansion`. These labels are useful when comparing whether a result came from raw semantic chunks, stored graph summaries, or query-time graph expansion.

Search Center direct ingestion uses background jobs for all three modes. The page should show "Ingestion job queued" quickly, then the Activity panel should show stage, heartbeat, progress, and any failure detail. Search failures should render the technical API error on the page instead of only saying that the search failed.

LazyGraphRAG traversal budgets are guardrails. If optional enrichment reaches a configured LLM/node/relationship/token limit, retrieval should stop expanding and return the best available results; it should not fail merely because the limit was reached.

### GraphRAG Smoke Test

After deployment:

1. Open `http://localhost:8081`.
2. Sign in with the seeded admin account.
3. Open Search Center.
4. Ingest a short test note in Semantic, GraphRAG, or LazyGraphRAG mode and confirm a job is queued.
5. Search for the topic from that note in Semantic mode.
6. Repeat in GraphRAG mode and confirm the expansion-hop control is visible.
7. Repeat in LazyGraphRAG mode and confirm the expansion-limit control is visible.
8. Confirm results include rank, score, retrieval strategy, citations, and source/chunk details.

Recent local Docker validation confirmed this flow with:

- Authenticated login through the Web UI.
- Background ingest returning a job through `/api/jobs/graphrag/ingest`.
- `GET /api/rags/retrieve`, `GET /api/graphrag/retrieve`, and `GET /api/lazygraphrag/retrieve` returning results from Search Center.
- `GET /api/graphrag/retrieve` returning summary-based results.
- `GET /api/lazygraphrag/retrieve` returning budgeted lazy expansion results without traversal-budget false failures.
- `/api/wiki/pages/{id}/status`, `/api/wiki/pages/{id}`, `/api/wiki/pages/{id}/history`, `/api/wiki/pages/{id}/related`, and `/api/wiki/regenerate/job` validating WRAGS lifecycle, edit/history, related-page, and background regeneration behavior.
- No browser console errors.

Upload and queued GraphRAG ingestion now use a faster searchable-first path: chunks and embeddings are created, lightweight source/chunk graph seed nodes are stored, and expensive graph enrichment is deferred to relevant query-time chunks. Legacy/full GraphRAG summary generation can still be slow; use the Activity panel or `GET /api/jobs` to confirm the job is alive, see the current stage, and inspect failures.

Taxonomy/Ontology explorers start with lightweight upload metadata and become richer as GraphRAG queries lazily enrich relevant chunks. Query-time discoveries are synced back to PostgreSQL through `ILazyEnrichmentKnowledgeSink`, so entities, tags, and relationships should appear after related GraphRAG searches or Copilot retrieval touch the source content.

Copilot answers display completion stats under each assistant response: elapsed seconds, estimated tokens per second, estimated completion tokens, retrieved context count, citation count, and a heuristic confidence percentage. For plan-based background executions, the Copilot page shows a progress panel with a status badge, progress bar, step checklist, heartbeats, elapsed time, partial and final results, and an execution telemetry card comparing actuals to the plan estimates. The confidence value is based on retrieval evidence and citations; it is not a calibrated correctness score.

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
