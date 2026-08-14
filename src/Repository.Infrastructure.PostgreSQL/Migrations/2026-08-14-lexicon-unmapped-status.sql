-- Sprint 71: lexicon governance — unmapped terms gain a review lifecycle so the admin surface can
-- show pending terms and mark them resolved (confirmed as an alias or dismissed). Run once against an
-- existing Aletheia deployment (fresh deployments get this from init.sql / PostgreSqlLexiconSchema).
-- Idempotent: safe to re-run.

ALTER TABLE lexicon_unmapped_terms
    ADD COLUMN IF NOT EXISTS status text NOT NULL DEFAULT 'pending',
    ADD COLUMN IF NOT EXISTS resolved_at timestamptz NULL;
