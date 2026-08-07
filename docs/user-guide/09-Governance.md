# 8. Governance & Collaboration

The Governance surface covers repository governance controls and collaboration features.

## What it includes

- **Governance dashboard** (`/governance`) — oversight of the knowledge estate: registered documents, ingestion health, and governance-relevant activity (see `docs/Governance` internals and the `CollaborationController`/`GovernanceController` APIs).
- **Collaboration** — shared, governed workflows over the repository (the Web Governance page groups these controls).

## Roles in practice

| Role | Can |
|---|---|
| Administrator | Everything, including user management, duplicate cleanup, re-embedding, operator modes |
| User | Upload, browse, search, wiki, Copilot |

## Audit and observability

- Uploads/ingestion jobs are tracked in the **Activity** panel and `/api/jobs`.
- Copilot responses carry telemetry; chat jobs expose progress records and telemetry endpoints (`/api/copilot/jobs`, `/api/copilot/jobs/{id}/progress`, `/api/copilot/jobs/{id}/telemetry`).
- RAGS status (`GET /api/rags/status`) exposes chunk/source counts, uncategorized ingests, extraction failures, and recent upload jobs (Administrator).

For API details see `docs/AdministratorGuide.md` and `docs/Copilot-Progress-API-Documentation.md`.