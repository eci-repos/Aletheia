using System.Text.Json;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;

namespace Aletheia.RAGS.Infrastructure.PostgreSQL.Knowledge;

public sealed class LazyEnrichmentKnowledgeSink : ILazyEnrichmentKnowledgeSink
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;

    public LazyEnrichmentKnowledgeSink(PostgreSqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<Result> RecordAsync(
        Guid sourceId,
        string? sourceName,
        IReadOnlyList<ExtractedEntity> entities,
        IReadOnlyList<ExtractedRelationship> relationships,
        CancellationToken cancellationToken = default)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Source ID is required.", nameof(sourceId));
        }

        if (entities.Count == 0 && relationships.Count == 0)
        {
            return Result.Success();
        }

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSchemaAsync(connection).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var entityCategoryId = await UpsertCategoryAsync(connection, transaction, "Entities").ConfigureAwait(false);
            var sourceEntityId = await UpsertOntologyEntityAsync(
                connection,
                transaction,
                GetSourceEntityName(sourceId, sourceName),
                "SourceDocument",
                new Dictionary<string, object>
                {
                    ["sourceId"] = sourceId,
                    ["sourceName"] = sourceName ?? string.Empty,
                    ["lastLazyEnrichedAt"] = DateTimeOffset.UtcNow
                }).ConfigureAwait(false);

            var ontologyIdsByExtractionId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in entities.Where(entity => !string.IsNullOrWhiteSpace(entity.Name)))
            {
                var tagId = await UpsertTagAsync(connection, transaction, entityCategoryId, entity.Name).ConfigureAwait(false);
                await UpsertTagSourceAsync(connection, transaction, tagId, sourceId, sourceName ?? string.Empty).ConfigureAwait(false);

                var ontologyId = await UpsertOntologyEntityAsync(
                    connection,
                    transaction,
                    entity.Name,
                    string.IsNullOrWhiteSpace(entity.Type) ? "Entity" : entity.Type,
                    new Dictionary<string, object>
                    {
                        ["confidence"] = entity.Confidence,
                        ["description"] = entity.Description ?? string.Empty,
                        ["lastSourceId"] = sourceId,
                        ["lastSourceName"] = sourceName ?? string.Empty,
                        ["lazyEnriched"] = true,
                        ["lastLazyEnrichedAt"] = DateTimeOffset.UtcNow
                    }).ConfigureAwait(false);

                ontologyIdsByExtractionId[entity.Id] = ontologyId;
                await UpsertOntologyRelationshipAsync(
                    connection,
                    transaction,
                    ontologyId,
                    sourceEntityId,
                    "found_in",
                    new Dictionary<string, object>
                    {
                        ["sourceId"] = sourceId,
                        ["sourceName"] = sourceName ?? string.Empty,
                        ["lazyEnriched"] = true
                    }).ConfigureAwait(false);
            }

            foreach (var relationship in relationships)
            {
                if (!ontologyIdsByExtractionId.TryGetValue(relationship.SourceId, out var sourceOntologyId) ||
                    !ontologyIdsByExtractionId.TryGetValue(relationship.TargetId, out var targetOntologyId))
                {
                    continue;
                }

                await UpsertOntologyRelationshipAsync(
                    connection,
                    transaction,
                    sourceOntologyId,
                    targetOntologyId,
                    NormalizeRelationshipType(relationship.Type),
                    new Dictionary<string, object>
                    {
                        ["confidence"] = relationship.Confidence,
                        ["description"] = relationship.Description ?? string.Empty,
                        ["sourceId"] = sourceId,
                        ["sourceName"] = sourceName ?? string.Empty,
                        ["lazyEnriched"] = true
                    }).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Lazy enrichment knowledge sync failed. {ex.Message}");
        }
    }

    private static async Task<Guid> UpsertCategoryAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        string name)
    {
        const string sql = @"
            INSERT INTO categories (name)
            VALUES (@Name)
            ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name
            RETURNING id";

        return await connection.ExecuteScalarAsync<Guid>(sql, new { Name = name }, transaction).ConfigureAwait(false);
    }

    private static async Task EnsureSchemaAsync(System.Data.IDbConnection connection)
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS taxonomy_tag_sources (
                tag_id UUID NOT NULL REFERENCES taxonomy_tags(id) ON DELETE CASCADE,
                source_id UUID NOT NULL,
                source_name TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                PRIMARY KEY(tag_id, source_id)
            );";

        await connection.ExecuteAsync(sql).ConfigureAwait(false);
    }

    private static async Task<Guid> UpsertTagAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        Guid categoryId,
        string name)
    {
        const string sql = @"
            INSERT INTO taxonomy_tags (category_id, name)
            VALUES (@CategoryId, @Name)
            ON CONFLICT (category_id, name) DO UPDATE SET name = EXCLUDED.name
            RETURNING id";

        return await connection.ExecuteScalarAsync<Guid>(sql, new { CategoryId = categoryId, Name = name }, transaction).ConfigureAwait(false);
    }

    private static async Task UpsertTagSourceAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        Guid tagId,
        Guid sourceId,
        string sourceName)
    {
        const string sql = @"
            INSERT INTO taxonomy_tag_sources (tag_id, source_id, source_name)
            VALUES (@TagId, @SourceId, @SourceName)
            ON CONFLICT (tag_id, source_id) DO UPDATE SET source_name = EXCLUDED.source_name";

        await connection.ExecuteAsync(sql, new
        {
            TagId = tagId,
            SourceId = sourceId,
            SourceName = sourceName
        }, transaction).ConfigureAwait(false);
    }

    private static async Task<Guid> UpsertOntologyEntityAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        string name,
        string entityType,
        IReadOnlyDictionary<string, object> properties)
    {
        const string sql = @"
            INSERT INTO ontology_entities (name, entity_type, properties)
            VALUES (@Name, @EntityType, @Properties::jsonb)
            ON CONFLICT (name)
            DO UPDATE SET
                entity_type = EXCLUDED.entity_type,
                properties = ontology_entities.properties || EXCLUDED.properties
            RETURNING id";

        return await connection.ExecuteScalarAsync<Guid>(sql, new
        {
            Name = name,
            EntityType = entityType,
            Properties = JsonSerializer.Serialize(properties)
        }, transaction).ConfigureAwait(false);
    }

    private static async Task UpsertOntologyRelationshipAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        Guid sourceEntityId,
        Guid targetEntityId,
        string relationshipType,
        IReadOnlyDictionary<string, object> properties)
    {
        const string sql = @"
            INSERT INTO ontology_relationships (source_entity_id, target_entity_id, relationship_type, properties)
            VALUES (@SourceEntityId, @TargetEntityId, @RelationshipType, @Properties::jsonb)
            ON CONFLICT (source_entity_id, target_entity_id, relationship_type)
            DO UPDATE SET properties = ontology_relationships.properties || EXCLUDED.properties";

        await connection.ExecuteAsync(sql, new
        {
            SourceEntityId = sourceEntityId,
            TargetEntityId = targetEntityId,
            RelationshipType = relationshipType,
            Properties = JsonSerializer.Serialize(properties)
        }, transaction).ConfigureAwait(false);
    }

    private static string GetSourceEntityName(Guid sourceId, string? sourceName)
    {
        return string.IsNullOrWhiteSpace(sourceName)
            ? $"Source:{sourceId}"
            : $"Source:{sourceId}:{sourceName}";
    }

    private static string NormalizeRelationshipType(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return "related_to";
        }

        var normalized = System.Text.RegularExpressions.Regex
            .Replace(type.Trim().ToLowerInvariant(), "[^a-z0-9]+", "_")
            .Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "related_to" : normalized;
    }
}
