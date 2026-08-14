-- Sprint 70: normalized lexicon + grounded facts — canonical concept registry (lexicon_concepts +
-- lexicon_aliases), verified document facts (document_facts), and the governance loop's unmapped
-- terms (lexicon_unmapped_terms). Run once against an existing Aletheia deployment (fresh
-- deployments get this from init.sql / PostgreSqlLexiconSchema). Idempotent: safe to re-run.
-- The seed rows mirror LexiconSeedData (RAGS.Abstractions) — a binding test keeps them in sync.

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
