# Changelog

All notable changes to Aletheia are documented here. Format follows Keep a Changelog conventions; versioning follows SemVer.

## [1.1.0] - 2026-08-06 (Release Candidate)

### Added (Sprints 55-58)

- **Document Briefs & End-User Wiki (Sprint 55)** — per-document plain-language Wiki briefs generated after ingestion (kind `DocumentBriefs`), Wiki search/recent surfaces, internal search gating (`FeatureFlags:ShowInternalSearch`, default false).
- **Duplicate Upload Detection & Document Update Flow (Sprint 56)** — SHA-256 content fingerprinting, HTTP 409 duplicate trap, update-with-`existingFileId` versioning, replace semantics on re-ingest, admin duplicate report.
- **Search Center Retrieval Quality (Sprint 57)** — `GET /api/rags/status` diagnostics, contextual empty-state messages, configurable embedding provider (Ollama) with Simple fallback, dimension auto-migration, Reembed background job, `RAGS:MinimumScore` + keyword fallback with `RetrievalStrategy` surfacing.
- **Session Knowledge Theme Filtering (Sprint 58)** — theme metadata on canonical templates (`Theme:` line), `file_metadata.template_name`/`theme` persistence, session-scoped theme filter through chat/plan payloads, retrieval enforcement (vector + keyword + engine tool paths), `GET /api/knowledge/themes`, Web theme picker + header chips.

### Fixed

- Ingestion job routing regression that sent upload jobs to document-brief generation instead of extraction/chunking/embeddings (Sprint 57).
- Plan approval no longer drops the session theme filter (Sprint 58).

### Changed

- `file_metadata` schema: added `template_name`, `theme` (idempotent migration `2026-08-06-file-metadata-template-theme.sql`; fresh installs via `init.sql`).
- `docs/doc-templates/3.0 - RFP Analysis.md` declares `Theme: Analysis`.
- Project version now set centrally via `Directory.Build.props` (`1.1.0`).

## [1.0.0] - 2026-07-22

Production Go-Live (RC2 certified). See `docs/release/` for the certification suite (checklist, signoffs, readiness reports, runbook).