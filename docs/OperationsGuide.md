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
