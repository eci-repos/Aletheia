-- Sprint 59: soften the canonical gate (template_status) + multi-theme (theme as text[]).
-- Run once against an existing Aletheia deployment (fresh deployments get this from init.sql).
-- Idempotent: safe to re-run.

ALTER TABLE file_metadata ADD COLUMN IF NOT EXISTS template_status TEXT;

-- Multi-theme: theme becomes a text[] set. Cast existing single values to a one-element array.
-- Guarded so re-running against an already-converted text[] column is a no-op.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'file_metadata' AND column_name = 'theme' AND data_type = 'text'
    ) THEN
        ALTER TABLE file_metadata ALTER COLUMN theme TYPE text[] USING (CASE WHEN theme IS NULL THEN NULL ELSE ARRAY[theme] END);
    END IF;
END $$;

-- The btree index on theme is useless for array overlap; replace with GIN.
DROP INDEX IF EXISTS idx_file_metadata_theme;
CREATE INDEX IF NOT EXISTS idx_file_metadata_theme ON file_metadata USING GIN (theme);

CREATE INDEX IF NOT EXISTS idx_file_metadata_template_status ON file_metadata(template_status);
