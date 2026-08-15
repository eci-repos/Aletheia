-- Sprint 73: ingestion guard-rails — last_ingested_at on file_metadata. Distinguishes "never
-- successfully ingested" (NULL → startup reconciliation candidate) from "checked and non-ingestable"
-- (set → leave alone). Set by RepositoryKnowledgeSourceIngestionService on completion. Run once
-- against an existing Aletheia deployment (fresh deployments get this from init.sql). Idempotent.

ALTER TABLE file_metadata ADD COLUMN IF NOT EXISTS last_ingested_at TIMESTAMPTZ;
