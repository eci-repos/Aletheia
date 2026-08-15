CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS file_metadata (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    file_id UUID NOT NULL,
    file_name TEXT NOT NULL,
    version TEXT,
    content_type TEXT NOT NULL,
    size_bytes BIGINT NOT NULL,
    uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    tags JSONB NOT NULL DEFAULT '{}',
    content_hash TEXT,
    template_name TEXT,
    theme TEXT[],
    template_status TEXT,
    created_at TIMESTAMPTZ,
    created_by_id TEXT,
    created_by_type TEXT,
    created_by_name TEXT,
    last_modified_at TIMESTAMPTZ,
    last_modified_by_id TEXT,
    last_modified_by_type TEXT,
    last_modified_by_name TEXT,
    last_ingested_at TIMESTAMPTZ
);

-- Allow only one NULL version per file, while allowing multiple named versions
CREATE UNIQUE INDEX IF NOT EXISTS idx_file_metadata_unique_version ON file_metadata (file_id, COALESCE(version, ''));

CREATE INDEX IF NOT EXISTS idx_file_metadata_file_id ON file_metadata(file_id);
CREATE INDEX IF NOT EXISTS idx_file_metadata_file_name ON file_metadata USING gin(to_tsvector('simple', file_name));
CREATE INDEX IF NOT EXISTS idx_file_metadata_content_hash ON file_metadata(content_hash);
CREATE INDEX IF NOT EXISTS idx_file_metadata_theme ON file_metadata USING GIN (theme);
CREATE INDEX IF NOT EXISTS idx_file_metadata_template_status ON file_metadata(template_status);

CREATE TABLE IF NOT EXISTS security_users (
    user_id TEXT PRIMARY KEY,
    username TEXT NOT NULL,
    normalized_username TEXT NOT NULL UNIQUE,
    email TEXT NOT NULL DEFAULT '',
    display_name TEXT NOT NULL DEFAULT '',
    password_hash TEXT NOT NULL,
    password_salt TEXT NOT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS security_user_roles (
    user_id TEXT NOT NULL REFERENCES security_users(user_id) ON DELETE CASCADE,
    role TEXT NOT NULL,
    PRIMARY KEY (user_id, role)
);

CREATE TABLE IF NOT EXISTS security_refresh_tokens (
    token_hash TEXT PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES security_users(user_id) ON DELETE CASCADE,
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_revoked BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE INDEX IF NOT EXISTS idx_security_refresh_tokens_user_id ON security_refresh_tokens(user_id);
CREATE INDEX IF NOT EXISTS idx_security_refresh_tokens_expires_at ON security_refresh_tokens(expires_at);

CREATE TABLE IF NOT EXISTS embeddings (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    chunk_id UUID NOT NULL UNIQUE,
    source_id UUID NOT NULL,
    content TEXT NOT NULL,
    embedding vector(128) NOT NULL,
    page_number INT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_embeddings_source_id ON embeddings(source_id);
CREATE INDEX IF NOT EXISTS idx_embeddings_embedding ON embeddings USING ivfflat (embedding vector_cosine_ops);

CREATE TABLE IF NOT EXISTS categories (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name TEXT NOT NULL UNIQUE,
    parent_id UUID REFERENCES categories(id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS taxonomy_tags (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    category_id UUID NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    UNIQUE(category_id, name)
);

CREATE TABLE IF NOT EXISTS taxonomy_tag_sources (
    tag_id UUID NOT NULL REFERENCES taxonomy_tags(id) ON DELETE CASCADE,
    source_id UUID NOT NULL,
    source_name TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY(tag_id, source_id)
);

CREATE TABLE IF NOT EXISTS ontology_entities (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name TEXT NOT NULL UNIQUE,
    entity_type TEXT NOT NULL,
    properties JSONB DEFAULT '{}',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS ontology_relationships (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    source_entity_id UUID NOT NULL REFERENCES ontology_entities(id) ON DELETE CASCADE,
    target_entity_id UUID NOT NULL REFERENCES ontology_entities(id) ON DELETE CASCADE,
    relationship_type TEXT NOT NULL,
    properties JSONB DEFAULT '{}',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(source_entity_id, target_entity_id, relationship_type)
);

-- Sprint 61: server-side settings — app_settings (global, admin-managed) + user_settings (per-user).
CREATE TABLE IF NOT EXISTS app_settings (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_by TEXT
);

CREATE TABLE IF NOT EXISTS user_settings (
    user_id TEXT NOT NULL,
    key TEXT NOT NULL,
    value TEXT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (user_id, key)
);

CREATE INDEX IF NOT EXISTS idx_user_settings_user_id ON user_settings(user_id);

-- Sprint 63: persisted LazyGraphRAG corpus index — term frequency / doc frequency / avg doc
-- length survive restart and multi-instance. The in-memory CorpusDiscoveryIndex remains the hot
-- path; these tables are a write-through / load-on-start store.
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

-- Sprint 70: normalized lexicon + grounded facts — canonical concept registry (lexicon_concepts +
-- lexicon_aliases), verified document facts (document_facts), and the governance loop's unmapped
-- terms (lexicon_unmapped_terms). The seed rows mirror LexiconSeedData (RAGS.Abstractions) — a
-- binding test keeps them in sync.
CREATE TABLE IF NOT EXISTS lexicon_concepts (
    concept_key text PRIMARY KEY,
    label text NOT NULL,
    value_pattern text NULL,
    template_scope text NULL
);

CREATE TABLE IF NOT EXISTS lexicon_aliases (
    concept_key text NOT NULL REFERENCES lexicon_concepts(concept_key) ON DELETE CASCADE,
    alias text NOT NULL,
    PRIMARY KEY (concept_key, alias)
);

CREATE TABLE IF NOT EXISTS document_facts (
    id bigserial PRIMARY KEY,
    source_id uuid NOT NULL,
    concept_key text NOT NULL,
    value text NOT NULL,
    source_span text NOT NULL,
    page_number integer NULL,
    offset_in_page integer NULL,
    status text NOT NULL DEFAULT 'verified',
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_document_facts_source_id ON document_facts (source_id);

CREATE TABLE IF NOT EXISTS lexicon_unmapped_terms (
    term text NOT NULL,
    source_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    status text NOT NULL DEFAULT 'pending',
    resolved_at timestamptz NULL,
    PRIMARY KEY (term, source_id)
);

INSERT INTO lexicon_concepts (concept_key, label, value_pattern, template_scope) VALUES
    ('due_date', 'Due date', 'date', NULL),
    ('budget', 'Budget', 'currency', NULL),
    ('page_limit', 'Page limit', 'number', NULL),
    ('vendor', 'Vendor', 'text', NULL),
    ('submission', 'Submission', 'text', NULL)
ON CONFLICT (concept_key) DO NOTHING;

INSERT INTO lexicon_aliases (concept_key, alias) VALUES
    ('due_date', 'due date'), ('due_date', 'bid due'), ('due_date', 'proposal due date'),
    ('due_date', 'submission due date'), ('due_date', 'deadline'), ('due_date', 'closing date'),
    ('due_date', 'submission deadline'), ('due_date', 'response due'), ('due_date', 'bid deadline'),
    ('due_date', 'proposal deadline'), ('due_date', 'end date'),
    ('budget', 'budget'), ('budget', 'total budget'), ('budget', 'funding amount'),
    ('budget', 'contract value'), ('budget', 'award amount'), ('budget', 'maximum amount'),
    ('budget', 'ceiling'),
    ('page_limit', 'page limit'), ('page_limit', 'maximum pages'), ('page_limit', 'page count'),
    ('page_limit', 'not to exceed'),
    ('vendor', 'vendor'), ('vendor', 'contractor'), ('vendor', 'supplier'),
    ('vendor', 'offeror'), ('vendor', 'bidder'), ('vendor', 'proposer'),
    ('submission', 'submission'), ('submission', 'proposal'), ('submission', 'bid'),
    ('submission', 'offer'), ('submission', 'response')
ON CONFLICT (concept_key, alias) DO NOTHING;
