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
    last_modified_by_name TEXT
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
