using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Npgsql;

namespace Aletheia.RAGS.Infrastructure.PostgreSQL.Wiki;

public sealed class PostgreSqlWikiSchema
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;

    public PostgreSqlWikiSchema(PostgreSqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
CREATE TABLE IF NOT EXISTS wiki_pages (
    id uuid PRIMARY KEY,
    topic text NOT NULL,
    topic_normalized text NOT NULL,
    title text NOT NULL,
    title_normalized text NOT NULL,
    summary text NOT NULL,
    source_ids jsonb NOT NULL DEFAULT '[]'::jsonb,
    citations jsonb NOT NULL DEFAULT '[]'::jsonb,
    generated_from text NOT NULL,
    version integer NOT NULL DEFAULT 1,
    status text NOT NULL,
    score real NOT NULL DEFAULT 0,
    rank integer NOT NULL DEFAULT 0,
    retrieval_strategy text NOT NULL,
    primary_source_id uuid NULL,
    chunk_index integer NULL,
    reviewed_by text NULL,
    reviewed_at timestamptz NULL,
    related_topics jsonb NOT NULL DEFAULT '[]'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE wiki_pages ADD COLUMN IF NOT EXISTS reviewed_by text NULL;
ALTER TABLE wiki_pages ADD COLUMN IF NOT EXISTS reviewed_at timestamptz NULL;
ALTER TABLE wiki_pages ADD COLUMN IF NOT EXISTS related_topics jsonb NOT NULL DEFAULT '[]'::jsonb;

CREATE TABLE IF NOT EXISTS wiki_page_history (
    id uuid PRIMARY KEY,
    page_id uuid NOT NULL,
    version integer NOT NULL,
    title text NOT NULL,
    summary text NOT NULL,
    status text NOT NULL,
    related_topics jsonb NOT NULL DEFAULT '[]'::jsonb,
    change_type text NOT NULL,
    changed_by text NULL,
    change_note text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_wiki_page_history_page_id
    ON wiki_page_history (page_id, version DESC, created_at DESC);

CREATE UNIQUE INDEX IF NOT EXISTS ux_wiki_pages_topic_title_source
    ON wiki_pages (topic_normalized, title_normalized, generated_from);

CREATE INDEX IF NOT EXISTS ix_wiki_pages_topic_normalized
    ON wiki_pages (topic_normalized);

CREATE INDEX IF NOT EXISTS ix_wiki_pages_updated_at
    ON wiki_pages (updated_at DESC);
";

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
