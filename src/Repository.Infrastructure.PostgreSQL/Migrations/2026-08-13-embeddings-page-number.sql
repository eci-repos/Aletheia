-- Sprint 67: chunk source locator — page_number on the embeddings table so a chunk can be
-- opened at the page it starts on. Populated by the page-aware extraction/chunking pipeline
-- (PDF) and the lightweight reembed flow. Run once against an existing Aletheia deployment
-- (fresh deployments get this from init.sql / PgVectorSchema). Idempotent: safe to re-run.

ALTER TABLE embeddings ADD COLUMN IF NOT EXISTS page_number INT;
