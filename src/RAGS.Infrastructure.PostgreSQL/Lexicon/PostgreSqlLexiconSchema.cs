using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Npgsql;

namespace Aletheia.RAGS.Infrastructure.PostgreSQL.Lexicon;

/// <summary>
/// Runtime schema creation for the normalized lexicon (idempotent). Mirrors the tables in
/// <c>scripts/init.sql</c> and the migration <c>2026-08-14-lexicon-and-facts.sql</c>; the
/// initializer is a safety net so the API self-heals if a deployment missed the migration.
/// </summary>
public sealed class PostgreSqlLexiconSchema
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;

    public PostgreSqlLexiconSchema(PostgreSqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
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
";

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
