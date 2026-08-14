using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;

namespace Aletheia.RAGS.Infrastructure.PostgreSQL.Lexicon;

/// <summary>
/// PostgreSQL persistence for the normalized lexicon: canonical concepts + aliases, verified
/// document facts, and the governance loop's unmapped terms. Tables are created by
/// <c>PostgreSqlLexiconSchema</c> / <c>scripts/init.sql</c> / the migration
/// <c>2026-08-14-lexicon-and-facts.sql</c>.
/// </summary>
public sealed class PostgreSqlLexiconRepository : ILexiconRepository
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;

    public PostgreSqlLexiconRepository(PostgreSqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<Result<IReadOnlyList<LexiconConcept>>> GetAllConceptsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"
SELECT c.concept_key AS ConceptKey, c.label AS Label, c.value_pattern AS ValuePattern,
       c.template_scope AS TemplateScope, a.alias AS Alias
FROM lexicon_concepts c
LEFT JOIN lexicon_aliases a ON a.concept_key = c.concept_key
ORDER BY c.concept_key, a.alias;";
            var rows = await connection.QueryAsync<ConceptRow>(sql).ConfigureAwait(false);

            var conceptMap = new Dictionary<string, LexiconConcept>(StringComparer.OrdinalIgnoreCase);
            var aliasMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (!conceptMap.TryGetValue(row.ConceptKey, out var concept))
                {
                    concept = new LexiconConcept
                    {
                        Key = row.ConceptKey,
                        Label = row.Label,
                        ValuePattern = row.ValuePattern,
                        TemplateScope = row.TemplateScope
                    };
                    conceptMap[row.ConceptKey] = concept;
                    aliasMap[row.ConceptKey] = new List<string>();
                }

                if (!string.IsNullOrEmpty(row.Alias))
                {
                    aliasMap[row.ConceptKey].Add(row.Alias);
                }
            }

            foreach (var pair in conceptMap)
            {
                pair.Value.Aliases = aliasMap[pair.Key];
            }

            return Result<IReadOnlyList<LexiconConcept>>.Success(conceptMap.Values.ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<LexiconConcept>>.Failure($"Lexicon load failed. {ex.Message}");
        }
    }

    public async Task<Result> UpsertConceptAsync(LexiconConcept concept, CancellationToken cancellationToken = default)
    {
        if (concept is null)
        {
            throw new ArgumentNullException(nameof(concept));
        }

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            const string upsertSql = @"
INSERT INTO lexicon_concepts (concept_key, label, value_pattern, template_scope)
VALUES (@Key, @Label, @ValuePattern, @TemplateScope)
ON CONFLICT (concept_key) DO UPDATE SET
    label = EXCLUDED.label,
    value_pattern = EXCLUDED.value_pattern,
    template_scope = EXCLUDED.template_scope;";
            await connection.ExecuteAsync(
                upsertSql,
                new { concept.Key, concept.Label, concept.ValuePattern, concept.TemplateScope },
                transaction).ConfigureAwait(false);

            const string deleteAliasesSql = "DELETE FROM lexicon_aliases WHERE concept_key = @Key;";
            await connection.ExecuteAsync(deleteAliasesSql, new { concept.Key }, transaction).ConfigureAwait(false);

            if (concept.Aliases is { Count: > 0 })
            {
                const string insertAliasSql = "INSERT INTO lexicon_aliases (concept_key, alias) VALUES (@Key, @Alias);";
                await connection.ExecuteAsync(
                    insertAliasSql,
                    concept.Aliases.Select(a => new { concept.Key, Alias = a }),
                    transaction).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Lexicon concept persistence failed. {ex.Message}");
        }
    }

    public async Task<Result> SaveFactsAsync(Guid sourceId, IReadOnlyList<DocumentFact> facts, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            // Replace semantics: re-ingestion (document updates, repairs) replaces facts instead of accumulating.
            const string deleteSql = "DELETE FROM document_facts WHERE source_id = @SourceId;";
            await connection.ExecuteAsync(deleteSql, new { SourceId = sourceId }, transaction).ConfigureAwait(false);

            if (facts is { Count: > 0 })
            {
                const string insertSql = @"
INSERT INTO document_facts (source_id, concept_key, value, source_span, page_number, offset_in_page, status)
VALUES (@SourceId, @ConceptKey, @Value, @SourceSpan, @PageNumber, @OffsetInPage, @Status);";
                await connection.ExecuteAsync(
                    insertSql,
                    facts.Select(f => new
                    {
                        SourceId = sourceId,
                        f.ConceptKey,
                        f.Value,
                        f.SourceSpan,
                        f.PageNumber,
                        f.OffsetInPage,
                        f.Status
                    }),
                    transaction).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Document fact persistence failed. {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<DocumentFact>>> GetFactsAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"
SELECT source_id AS SourceId, concept_key AS ConceptKey, value AS Value, source_span AS SourceSpan,
       page_number AS PageNumber, offset_in_page AS OffsetInPage, status AS Status
FROM document_facts
WHERE source_id = @SourceId
ORDER BY concept_key;";
            var rows = await connection.QueryAsync<DocumentFact>(sql, new { SourceId = sourceId }).ConfigureAwait(false);
            return Result<IReadOnlyList<DocumentFact>>.Success(rows.ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<DocumentFact>>.Failure($"Document fact load failed. {ex.Message}");
        }
    }

    public async Task<Result> RecordUnmappedTermAsync(string term, Guid sourceId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"
INSERT INTO lexicon_unmapped_terms (term, source_id)
VALUES (@Term, @SourceId)
ON CONFLICT (term, source_id) DO NOTHING;";
            await connection.ExecuteAsync(sql, new { Term = term, SourceId = sourceId }).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Unmapped term persistence failed. {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<UnmappedTerm>>> GetUnmappedTermsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"
SELECT term AS Term, source_id AS SourceId, created_at AS CreatedAt
FROM lexicon_unmapped_terms
ORDER BY created_at DESC;";
            var rows = await connection.QueryAsync<UnmappedTerm>(sql).ConfigureAwait(false);
            return Result<IReadOnlyList<UnmappedTerm>>.Success(rows.ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<UnmappedTerm>>.Failure($"Unmapped term load failed. {ex.Message}");
        }
    }

    private sealed class ConceptRow
    {
        public string ConceptKey { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public string? ValuePattern { get; init; }
        public string? TemplateScope { get; init; }
        public string? Alias { get; init; }
    }
}
