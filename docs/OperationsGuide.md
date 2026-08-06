# Operations Guide

## Service Management

### Start / Stop / Restart

```bash
# Start all services
docker compose up -d

# Stop all services
docker compose down

# Restart a specific service
docker compose restart aletheia-api
docker compose restart aletheia-web
docker compose restart postgres
docker compose restart neo4j
docker compose restart minio
```

### View Logs

```bash
# All services
docker compose logs -f

# Specific service
docker compose logs -f aletheia-api
docker compose logs -f postgres
docker compose logs -f neo4j
docker compose logs -f minio
```

## Backup and Restore

### PostgreSQL (Relational Data + Vectors)

```bash
# Backup
docker compose exec postgres pg_dump -U aletheia aletheia > backup-$(date +%F).sql

# Restore
docker compose exec -T postgres psql -U aletheia aletheia < backup-YYYY-MM-DD.sql
```

### Neo4j (Knowledge Graph)

```bash
# Backup (using neo4j-admin)
docker compose exec neo4j neo4j-admin database dump neo4j --to-path=/backups

# Restore
docker compose exec neo4j neo4j-admin database load neo4j --from-path=/backups
```

### MinIO (Object Storage)

Use `mc` (MinIO Client) or the MinIO Console (`http://localhost:9001`) to create bucket snapshots.

## Monitoring

### Resource Usage

```bash
docker stats
```

### Database Metrics

- PostgreSQL: Query `pg_stat_activity` and `pg_stat_user_tables` for active connections and table statistics.
- Neo4j: Open the Neo4j Browser at `http://localhost:7474` and run `CALL dbms.listQueries()` to inspect running queries.

### GraphRAG and LazyGraphRAG Runtime Checks

- Watch `/api/jobs` and the Web Activity panel for background ingestion stage, heartbeat, progress, and failures. Upload jobs now seed graph chunks without full document-wide LLM summarization, so they should complete much faster than the earlier summary-heavy path.
- Confirm GraphRAG retrieval returns summary strategies such as `summary-entity` and `summary-community` when summaries exist.
- In Copilot, review the stats under each assistant answer for elapsed time, estimated token throughput, retrieved context count, citations, and heuristic confidence.
- Monitor LazyGraphRAG retrieval for unexpected budget failures. Budget limits should stop optional expansion and return the best available result; a visible traversal-budget error indicates a defect or a hard timeout.
- Use Search Center for a quick operator smoke test after deployment: sign in, run Semantic, WRAGS, GraphRAG, and LazyGraphRAG searches against known content, and verify results include rank, score, retrieval strategy, citations, and no browser console errors.
- Use WRAGS Wiki for a quick knowledge-facing smoke test: search a known topic and verify durable wiki entries show citations, version, lifecycle status, retrieval strategy, related topics, related pages, source ID, chunk index, history, and updated time. Queue regeneration to confirm a background job appears, edit a page to confirm history advances, then mark a page Reviewed/Approved/Stale to confirm lifecycle updates.

### Log Rotation

Docker Compose does not rotate logs by default. Configure the Docker daemon or use the `json-file` log driver with limits:

```yaml
logging:
  driver: "json-file"
  options:
    max-size: "10m"
    max-file: "3"
```

## Maintenance Tasks

### Vacuum PostgreSQL

```bash
docker compose exec postgres psql -U aletheia -c "VACUUM ANALYZE;"
```

### Reindex Vectors

```bash
docker compose exec postgres psql -U aletheia -c "REINDEX INDEX idx_embedding;"
```

### Neo4j Consistency Check

```bash
docker compose exec neo4j neo4j-admin database check neo4j
```

### Graph Summary Refresh

GraphRAG summaries can be generated during full enrichment and stored in graph metadata. File uploads now use lightweight graph seed indexing by default; query-time GraphRAG can lazily enrich relevant chunks when summaries are absent. If source content, extracted entities, relationships, or community assignments are regenerated, run a fresh full GraphRAG ingest or future summary refresh job so community/global answers do not rely on stale summaries.

### Document Briefs (End-User Wiki)

The user-facing Wiki (/wiki) shows document briefs: plain-language, per-document summaries that open with the document's nature/purpose and follow the canonical template's ordered sections, grounded and cited. Briefs are stored as wiki_pages rows with generated_from = 'document-brief'; community summaries (generated_from = 'graphrag') are excluded from end-user search/list output.

Briefs are generated automatically after ingestion (after EnsureIngestedAsync succeeds and after upload ingestion jobs) and can be regenerated on demand:

`ash
# Regenerate briefs for all registered documents
curl -X POST http://localhost:8080/api/wiki/briefs/regenerate -H "Authorization: Bearer <token>" -H "Content-Type: application/json" -d '{}'

# Regenerate the brief for one document
curl -X POST http://localhost:8080/api/wiki/briefs/regenerate -H "Authorization: Bearer <token>" -H "Content-Type: application/json" -d '{"sourceId":"<source-id>","sourceName":"<file name>"}'
`

The response is an IngestionJobSnapshot (kind DocumentBriefs); watch progress in the Activity panel or GET /api/jobs. Documents without a canonical template are skipped (the ingestion gate already prevents them).

Internal retrieval surfaces (raw Wiki/WRAGS modes, GraphRAG, LazyGraphRAG, global-graph search) are hidden from end users by default. Set FeatureFlags:ShowInternalSearch=true (or FeatureFlags__ShowInternalSearch=true) to re-enable them for admin/diagnostics work.

### WRAGS Page Lifecycle

WRAGS pages are durable generated or edited knowledge snapshots. Operators can edit page bodies, view history, queue regeneration jobs, and mark pages `Reviewed`, `Approved`, `NeedsReview`, or `Stale` from `/wiki`; the API persists review metadata, stale flags, and revision history in PostgreSQL. Regeneration creates a fresh generated snapshot, stores the previous revision in history, and clears prior review metadata for the matching topic/title/mode. Pages are flagged stale when linked Repository source metadata is newer than the wiki page update time.

## Security

- Update base images monthly: `docker compose pull && docker compose up -d`
- Rotate passwords periodically via environment variables and restart services.
- Restrict exposed ports using a reverse proxy (nginx, Traefik) or cloud load balancer.
- Enable TLS at the reverse proxy layer for all public endpoints.
- Run `dotnet list package --vulnerable` before each release to check for new CVEs.

## Incident Response

1. Isolate the affected service: `docker compose stop <service>`
2. Review logs: `docker compose logs <service>`
3. Restore from the latest backup if data corruption is suspected.
4. Restart services and verify health endpoints.
5. For GraphRAG incidents, verify Neo4j connectivity, graph driver version alignment, and whether a background job is still running or failed in `/api/jobs`.

### Duplicate Uploads and Document Updates (Sprint 56)

- **Duplicate trap**: uploads are fingerprinted (SHA-256) server-side. Re-posting an identical file is rejected with HTTP 409 and no blob/metadata/ingestion/brief is created; the Web UI shows a "Duplicate - already exists" badge. Nothing to operate.
- **Document updates**: submitting a changed file against an existing document keeps its fileId, snapshots a named version, replaces the blob/current metadata, re-ingests the same source (embeddings, knowledge-index rows, and graph nodes are replaced), and regenerates the Wiki brief. Monitor the upload ingestion job (`/api/jobs`) to completion; a failure leaves the current version in place and reports the error in the job.
- **Admin duplicate report**: `GET /api/files/duplicates` (Administrator role) lists rows sharing a content hash. Review and remove duplicates manually with the existing DELETE flow; deletion also removes vectors, knowledge-index rows, and metadata.
- **Schema change for existing deployments**: run `src/Repository.Infrastructure.PostgreSQL/Migrations/2026-08-05-file-metadata-content-hash.sql` once (idempotent) to add `content_hash` and its index. Pre-existing rows are re-fingerprinted on their next upload/update.

### Search Center Troubleshooting (Sprint 56/57)

The Search Center's default **Semantic** mode searches the `embeddings` table (pgvector). A query that returns **zero results almost always means there are no embeddings** for the corpus: `PgVectorStore.SearchAsync` has no similarity threshold, so any embedded chunk would rank in the top-K (even a weak match). Causes and checks:

1. **Ingestion job did not complete.** Open the Activity panel or `GET /api/jobs`; the `Upload` job must reach `Succeeded` ("Ready - fully ingested and searchable"). A failed job shows the reason (template gate, extraction, AI provider).
2. **Canonical template gate.** Ingestion is stopped when the document name does not token-match a template in `docs/doc-templates` (e.g., `CMP 2026 - 3. RFP Analysis.docx` -> `3.0 - RFP Analysis`). Renamed copies are silently blocked.
3. **No extractable text.** If extraction yields no text, ingestion marks the source "not ingestable" and writes no embeddings.
4. **Fresh database / stack restart with a new volume** - embeddings are gone even when blobs/metadata still exist.

Status endpoint (Sprint 57): `GET /api/rags/status` returns `EmbeddedChunkCount`, `IngestedSourceCount`, `RegisteredDocumentCount`, `TemplateGateSkipCount`, `ExtractionFailureCount`, recent template-gate skips, and the last 10 `UploadIngestion` jobs with status/error. The Search Center uses it to tell users why a search returned nothing (empty corpus vs no match); an operator status chip is shown when `FeatureFlags:ShowInternalSearch=true`.

Verification query (run against PostgreSQL):

```sql
SELECT count(*) AS embedded_chunks FROM embeddings;
SELECT source_id, count(*) FROM embeddings GROUP BY source_id;
```

Example queries that should return results for an ingested RFP Analysis document: `Scope of Work`, `Project Summary`, `proposal format`, `Vendor Experience`, `requirements`, `Work Plan`.

**Embedding caveat:** today embeddings use the deterministic `SimpleEmbeddingProvider` (character + bigram frequency hash, 128-dim), so matching is lexical rather than meaning-based. Improving retrieval quality (real embedding provider, score floor, keyword fallback) is planned in `docs/sprints/Sprint-57 - Search Center Retrieval Quality and Troubleshooting.md`.


### Keyword Fallback (Sprint 57)

`RagsService.RetrieveAsync` now falls back to keyword search (`IVectorStore.SearchKeywordAsync`; PostgreSQL `ILIKE` over chunk content and file name, newest chunks first) when the vector search returns no results or the best vector score is below `RAGS:MinimumScore` (default 0). Results carry `RetrievalStrategy` = `keyword` so callers can tell which path produced them. Tune via `RAGS__MinimumScore` (e.g., 0.35) to prefer lexical results when vector matches are weak.

### Re-embedding (Sprint 57)

`POST /api/jobs/rags/reembed` queues a `ReembedIngestion` job that re-runs ingestion for every registered document (embeddings, knowledge-index rows, and graph nodes replaced; Wiki briefs regenerated). Use it after changing the embedding provider or dimension. The schema initializer migrates the `embeddings.embedding` column dimension automatically (drops/recreates the vector index). Configure via `AI:EmbeddingProvider` ("Simple" default | "Ollama"), `AI:EmbeddingDimension`, and the provider's `EmbeddingModel`.

### Knowledge Theme Filtering (Sprint 58)

- A Copilot session can be scoped to knowledge themes (Analysis, As-Built, As-Proposed, ...). Themes come from the canonical templates (`docs/doc-templates`, first-line `Theme: ...`) and are persisted per document at ingestion.
- **Symptom: Copilot returns nothing for a document that exists.** Check the session header chips: if a theme is active, the document may be outside the selected themes. Remove the theme (Edit next to the chips, then "All themes") and retry.
- **Symptom: `GET /api/knowledge/themes` shows `Uncategorized` for a document.** The document name does not match a canonical template (or its template lacks a `Theme:` line). Register/update the template and re-ingest (or run the Reembed job) to persist the theme.
- Verify the persisted mapping: `SELECT file_name, template_name, theme FROM file_metadata ORDER BY uploaded_at DESC;`
- Themes with zero documents are still listed; selecting them simply matches nothing.