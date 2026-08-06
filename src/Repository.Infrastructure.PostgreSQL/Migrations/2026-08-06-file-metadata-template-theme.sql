-- Sprint 58: knowledge theme filtering - persist the canonical template + theme per document.
-- Run once against an existing Aletheia deployment (fresh deployments get this from init.sql).
-- Idempotent: safe to re-run.

ALTER TABLE file_metadata ADD COLUMN IF NOT EXISTS template_name TEXT;
ALTER TABLE file_metadata ADD COLUMN IF NOT EXISTS theme TEXT;
CREATE INDEX IF NOT EXISTS idx_file_metadata_theme ON file_metadata(theme);