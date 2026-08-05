# Deployment Guide

## Prerequisites

- Docker Engine 28.3.0+ and Docker Compose (v2)
- .NET 10 SDK (for local development or CI builds)
- PostgreSQL 16+ with pgvector extension (or use the provided Docker image)
- Neo4j 5.x (or use the provided Docker image)
- MinIO RELEASE.2024-03-03 (or use the provided Docker image)

## Quick Start (Docker Compose)

The fastest way to deploy the full stack is with Docker Compose:

```bash
# From the repository root
docker compose up -d
```

This starts the following services:

| Service       | Image                     | Default Port | Purpose                          |
|---------------|---------------------------|--------------|----------------------------------|
| aletheia-api  | Built from `Dockerfile`   | 8080         | ASP.NET Core Web API             |
| aletheia-web  | Built from `Dockerfile`   | 8080         | Blazor WebAssembly UI            |
| postgres      | `pgvector/pgvector:pg16`  | 5432         | Relational DB + vector store     |
| neo4j         | `neo4j:5`                 | 7474 / 7687  | Knowledge graph                  |
| minio         | `minio/minio:latest`      | 9000 / 9001  | Object storage                   |

The API image installs the native `libgssapi-krb5-2` package because Npgsql can load GSSAPI/Kerberos libraries at runtime in Linux containers. The graph stack also requires aligned Neo4j driver versions across `RAGS.Infrastructure.Graph` and `KnowledgeGraph.Infrastructure.Neo4j`.

## Configuration

### Environment Variables

Create a `.env` file in the repository root or set the following environment variables before starting:

```env
# PostgreSQL
POSTGRES_DB=aletheia
POSTGRES_USER=aletheia
POSTGRES_PASSWORD=UseASecurePasswordInProduction

# Neo4j
NEO4J_AUTH=neo4j/UseASecurePasswordInProduction

# MinIO
MINIO_ROOT_USER=aletheia
MINIO_ROOT_PASSWORD=UseASecurePasswordInProduction

# ASP.NET Core
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
ALETHEIA_ADMIN_PASSWORD=UseASecureAdminPasswordInProduction
ChatAgent__OrchestrationScriptPath=Prompts/copilot-rags-orchestration.md
```

> **Security Note**: Change all default passwords before deploying to any non-local environment.

### Connection Strings

When running outside Docker, update `appsettings.Production.json` (never commit secrets):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=postgres;Port=5432;Database=aletheia;Username=aletheia;Password=<password>",
    "Neo4jConnection": "bolt://neo4j:7687",
    "MinIO": "http://minio:9000"
  }
}
```

## Build and Push Images (CI/CD)

The GitHub Actions workflow (`.github/workflows/ci.yml`) handles restore, build, and test stages. Extend it with publish and push steps:

```yaml
- name: Publish
  run: dotnet publish src/Repository.API/Repository.API.csproj -c Release -o ./pub/api

- name: Publish Web
  run: dotnet publish src/Aletheia.Web/Aletheia.Web.csproj -c Release -o ./pub/web

- name: Docker Build & Push
  run: |
    docker build -t ghcr.io/<org>/aletheia-api:${{ github.sha }} -f src/Repository.API/Dockerfile .
    docker build -t ghcr.io/<org>/aletheia-web:${{ github.sha }} -f src/Aletheia.Web/Dockerfile .
    docker push ghcr.io/<org>/aletheia-api:${{ github.sha }}
    docker push ghcr.io/<org>/aletheia-web:${{ github.sha }}
```

## Scaling Considerations

- **API**: Stateless; scale horizontally behind a load balancer.
- **Web (Blazor WebAssembly)**: Static frontend served by nginx; scale as static web assets behind a CDN or reverse proxy.
- **PostgreSQL**: Use a managed instance (e.g., AWS RDS, Azure Database) or a primary-replica setup for production.
- **Neo4j**: Neo4j Aura or a clustered deployment for HA.
- **MinIO**: Use MinIO in distributed mode for production object storage.

## Health Checks

After deployment, verify service health:

```bash
# API health check
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready

# PostgreSQL readiness
docker compose exec postgres pg_isready -U aletheia

# Neo4j readiness
curl -u neo4j:<password> http://localhost:7474/dbms/health
```

## Post-Deploy UI Smoke Test

1. Open `http://localhost:8081`.
2. Sign in as the seeded `admin` account using `ALETHEIA_ADMIN_PASSWORD`.
3. Open Search Center.
4. Ingest a short test note and confirm the page reports a queued ingestion job.
5. Search for the note topic in Semantic mode.
6. Open Search Center in WRAGS mode and confirm saved wiki pages can be returned as retrieval results.
7. Confirm that results render with rank, score, retrieval strategy, citations, and source/chunk details.
8. Open WRAGS and search the same topic; confirm durable wiki entries render with citations, version, lifecycle status, retrieval strategy, related topics/pages, history, and source/chunk details.
9. Edit one WRAGS page, confirm the version/history updates, queue regeneration, then mark the page Reviewed/Approved/Stale and confirm lifecycle changes appear.

GraphRAG and LazyGraphRAG are active Web UI workflows in Search Center and WRAGS Wiki. Broad Copilot retrieval can use GraphRAG first and LazyGraphRAG second before falling back to Semantic RAGS.

The Web UI and nginx `/api` proxy are configured with 30-minute timeouts for long-running Copilot chat responses. Ingestion should normally return quickly with a job id while enrichment continues in the API worker.

LazyGraphRAG traversal budgets are expected to stop optional graph expansion, not fail the whole query, when testing the backend APIs directly.

## Troubleshooting

| Symptom                        | Possible Cause              | Resolution                                    |
|--------------------------------|-----------------------------|-----------------------------------------------|
| 500 errors on startup          | Missing DB connection       | Ensure PostgreSQL and pgvector are initialized |
| Vector search returns nothing  | pgvector extension missing  | Run `CREATE EXTENSION IF NOT EXISTS vector;`  |
| MinIO upload fails             | Bucket does not exist       | Create bucket via MinIO Console (`:9001`)     |
| Neo4j connection refused       | Neo4j still starting        | Wait for Neo4j to report `Server started`     |
| API readiness fails after start | Native DB dependency missing or backing service unhealthy | Check API logs, verify `libgssapi-krb5-2` is in the image, and confirm PostgreSQL/Neo4j/MinIO health |
| GraphRAG retrieve throws Neo4j method errors | Neo4j driver mismatch | Align `Neo4j.Driver` package versions across graph projects |
| GraphRAG ingest appears slow | Summary generation is running in a background worker | Watch the Web Activity panel, poll `GET /api/jobs`, and check `docker compose logs -f aletheia-api` |
