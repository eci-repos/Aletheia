-- Sprint 63: persisted LazyGraphRAG corpus index — term frequency / doc frequency / avg doc
-- length survive restart and multi-instance. The in-memory CorpusDiscoveryIndex remains the hot
-- path; these tables are a write-through / load-on-start store. Run once against an existing
-- Aletheia deployment (fresh deployments get this from init.sql). Idempotent: safe to re-run.

CREATE TABLE IF NOT EXISTS lazygraphrag_corpus_documents (
    source_id UUID PRIMARY KEY,
    document_length INTEGER NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS lazygraphrag_corpus_terms (
    source_id UUID NOT NULL REFERENCES lazygraphrag_corpus_documents(source_id) ON DELETE CASCADE,
    term TEXT NOT NULL,
    frequency INTEGER NOT NULL,
    PRIMARY KEY (source_id, term)
);

CREATE INDEX IF NOT EXISTS idx_lazygraphrag_corpus_terms_term ON lazygraphrag_corpus_terms(term);
