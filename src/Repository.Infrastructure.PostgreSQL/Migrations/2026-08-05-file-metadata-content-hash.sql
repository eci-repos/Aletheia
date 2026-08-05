-- Sprint 56: content fingerprinting for duplicate-upload detection.
-- Run once against an existing Aletheia deployment (fresh deployments get this from init.sql).
-- Idempotent: safe to re-run.

ALTER TABLE file_metadata ADD COLUMN IF NOT EXISTS content_hash TEXT;
CREATE INDEX IF NOT EXISTS idx_file_metadata_content_hash ON file_metadata(content_hash);
