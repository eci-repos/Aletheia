using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.Pipelines;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;

namespace Aletheia.Repository.API.Services;

public interface IUploadedContentKnowledgeIndexer
{
    Task<Result> IndexAsync(
        Guid sourceId,
        string sourceName,
        string content,
        CancellationToken cancellationToken = default);

    Task<Result> IndexAsync(
        Guid sourceId,
        string sourceName,
        string content,
        IIngestionProgressSink? progress,
        CancellationToken cancellationToken = default);

    Task<Result> IndexLightweightAsync(
        Guid sourceId,
        string sourceName,
        string content,
        IIngestionProgressSink? progress,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteSourceAsync(Guid sourceId, CancellationToken cancellationToken = default);
}

public sealed class UploadedContentKnowledgeIndexer : IUploadedContentKnowledgeIndexer
{
    private const int MaxEntities = 30;
    private const int MaxTopics = 20;
    private const int MaxGraphChunks = 250;

    private static readonly Regex WordPattern = new(@"\b[\p{L}\p{N}][\p{L}\p{N}\-]{2,}\b", RegexOptions.Compiled);
    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly IEntityExtractionService _entityExtraction;
    private readonly IRelationshipExtractionService _relationshipExtraction;
    private readonly IGraphProvider _graphProvider;
    private readonly IGraphSummaryService _graphSummary;
    private readonly ICommunityDetectionService _communityDetection;
    private readonly ChunkingPipeline _chunkingPipeline;
    private readonly ITermNormalizer _termNormalizer;

    public UploadedContentKnowledgeIndexer(
        PostgreSqlConnectionFactory connectionFactory,
        IEntityExtractionService entityExtraction,
        IRelationshipExtractionService relationshipExtraction,
        IGraphProvider graphProvider,
        IGraphSummaryService graphSummary,
        ICommunityDetectionService communityDetection,
        ChunkingPipeline chunkingPipeline,        ITermNormalizer termNormalizer)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _entityExtraction = entityExtraction ?? throw new ArgumentNullException(nameof(entityExtraction));
        _relationshipExtraction = relationshipExtraction ?? throw new ArgumentNullException(nameof(relationshipExtraction));
        _graphProvider = graphProvider ?? throw new ArgumentNullException(nameof(graphProvider));
        _graphSummary = graphSummary ?? throw new ArgumentNullException(nameof(graphSummary));
        _communityDetection = communityDetection ?? throw new ArgumentNullException(nameof(communityDetection));
        _chunkingPipeline = chunkingPipeline ?? throw new ArgumentNullException(nameof(chunkingPipeline));        _termNormalizer = termNormalizer ?? throw new ArgumentNullException(nameof(termNormalizer));
    }

    public async Task<Result> IndexAsync(
        Guid sourceId,
        string sourceName,
        string content,
        CancellationToken cancellationToken = default)
    {
        return await IndexAsync(sourceId, sourceName, content, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> IndexAsync(
        Guid sourceId,
        string sourceName,
        string content,
        IIngestionProgressSink? progress,
        CancellationToken cancellationToken = default)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Source ID is required.", nameof(sourceId));
        }

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("Source name is required.", nameof(sourceName));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return Result.Success();
        }

        try
        {
            progress?.Report("Knowledge schema", "Ensuring taxonomy and ontology schema is ready.", 58, force: true);
            await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);

            progress?.Report("Entity discovery", "Discovering document-level entities and topics.", 60, force: true);
            var indexedText = $"{sourceName} {content}";
            var extractedEntities = await DiscoverEntitiesAsync(indexedText, cancellationToken).ConfigureAwait(false);
            var topics = ExtractTopics(indexedText, extractedEntities.Select(e => e.Name));

            progress?.Report("Taxonomy and ontology", "Persisting extracted topics, entities, and relationships.", 64, force: true);
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var topicCategoryId = await UpsertCategoryAsync(connection, transaction, "Topics").ConfigureAwait(false);
            var entityCategoryId = await UpsertCategoryAsync(connection, transaction, "Entities").ConfigureAwait(false);

            foreach (var topic in topics)
            {
                var tagId = await UpsertTagAsync(connection, transaction, topicCategoryId, topic).ConfigureAwait(false);
                await UpsertTagSourceAsync(connection, transaction, tagId, sourceId, sourceName).ConfigureAwait(false);
            }

            foreach (var entity in extractedEntities.Take(MaxEntities))
            {
                var tagId = await UpsertTagAsync(connection, transaction, entityCategoryId, entity.Name).ConfigureAwait(false);
                await UpsertTagSourceAsync(connection, transaction, tagId, sourceId, sourceName).ConfigureAwait(false);
            }

            var sourceEntityId = await UpsertOntologyEntityAsync(
                connection,
                transaction,
                GetSourceEntityName(sourceId, sourceName),
                "SourceDocument",
                new Dictionary<string, object>
                {
                    ["sourceId"] = sourceId,
                    ["sourceName"] = sourceName
                }).ConfigureAwait(false);

            var entityIds = new List<Guid>();
            foreach (var entity in extractedEntities.Take(MaxEntities))
            {
                var entityId = await UpsertOntologyEntityAsync(
                    connection,
                    transaction,
                    entity.Name,
                    string.IsNullOrWhiteSpace(entity.Type) ? "Entity" : entity.Type,
                    new Dictionary<string, object>
                    {
                        ["confidence"] = entity.Confidence,
                        ["description"] = entity.Description ?? string.Empty,
                        ["lastSourceId"] = sourceId,
                        ["lastSourceName"] = sourceName
                    }).ConfigureAwait(false);

                entityIds.Add(entityId);
                await UpsertOntologyRelationshipAsync(
                    connection,
                    transaction,
                    entityId,
                    sourceEntityId,
                    "found_in",
                    new Dictionary<string, object> { ["sourceId"] = sourceId }).ConfigureAwait(false);
            }

            foreach (var pair in BuildEntityPairs(entityIds.Take(10).ToList()))
            {
                await UpsertOntologyRelationshipAsync(
                    connection,
                    transaction,
                    pair.SourceId,
                    pair.TargetId,
                    "co_occurs_with",
                    new Dictionary<string, object> { ["sourceId"] = sourceId }).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            progress?.Report("Graph persistence", "Persisting source, chunk, entity, and relationship graph intelligence.", 70, force: true);
            await PersistGraphIntelligenceAsync(
                sourceId,
                sourceName,
                content,
                progress,
                cancellationToken).ConfigureAwait(false);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Knowledge indexing failed. {ex.Message}");
        }
    }

    public async Task<Result> IndexLightweightAsync(
        Guid sourceId,
        string sourceName,
        string content,
        IIngestionProgressSink? progress,
        CancellationToken cancellationToken = default)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Source ID is required.", nameof(sourceId));
        }

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("Source name is required.", nameof(sourceName));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return Result.Success();
        }

        try
        {
            progress?.Report("Knowledge seed", "Recording searchable topics and graph seed nodes without LLM enrichment.", 58, force: true);
            await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);

            var topics = ExtractTopics($"{sourceName} {content}", Array.Empty<string>());
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var topicCategoryId = await UpsertCategoryAsync(connection, transaction, "Topics").ConfigureAwait(false);
            foreach (var topic in topics)
            {
                var tagId = await UpsertTagAsync(connection, transaction, topicCategoryId, topic).ConfigureAwait(false);
                await UpsertTagSourceAsync(connection, transaction, tagId, sourceId, sourceName).ConfigureAwait(false);
            }

            var sourceEntityId = await UpsertOntologyEntityAsync(
                connection,
                transaction,
                GetSourceEntityName(sourceId, sourceName),
                "SourceDocument",
                new Dictionary<string, object>
                {
                    ["sourceId"] = sourceId,
                    ["sourceName"] = sourceName,
                    ["ingestionMode"] = "lazy-enrichment-seed",
                    ["lazyEnrichmentStatus"] = "Pending"
                }).ConfigureAwait(false);

            foreach (var topic in topics)
            {
                var topicEntityId = await UpsertOntologyEntityAsync(
                    connection,
                    transaction,
                    topic,
                    "Topic",
                    new Dictionary<string, object>
                    {
                        ["sourceId"] = sourceId,
                        ["sourceName"] = sourceName,
                        ["ingestionMode"] = "lazy-enrichment-seed",
                        ["lazyEnrichmentStatus"] = "Pending"
                    }).ConfigureAwait(false);

                await UpsertOntologyRelationshipAsync(
                    connection,
                    transaction,
                    topicEntityId,
                    sourceEntityId,
                    "found_in",
                    new Dictionary<string, object>
                    {
                        ["sourceId"] = sourceId,
                        ["sourceName"] = sourceName
                    }).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            await PersistGraphSeedAsync(sourceId, sourceName, content, progress, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Lightweight knowledge indexing failed. {ex.Message}");
        }
    }

    private async Task PersistGraphSeedAsync(
        Guid sourceId,
        string sourceName,
        string content,
        IIngestionProgressSink? progress,
        CancellationToken cancellationToken)
    {
        var sourceNode = new GraphNode(
            sourceId.ToString(),
            sourceName,
            "Source",
            new Dictionary<string, object>
            {
                ["sourceId"] = sourceId.ToString(),
                ["sourceName"] = sourceName,
                ["content"] = content,
                ["ingestionMode"] = "lazy-enrichment-seed",
                ["lazyEnrichmentStatus"] = "Pending"
            });

        await _graphProvider.CreateNodeAsync(sourceNode, cancellationToken).ConfigureAwait(false);

        var chunks = _chunkingPipeline.Chunk(sourceId, content).Take(MaxGraphChunks).ToList();
        var chunkCount = Math.Max(chunks.Count, 1);
        var processedChunks = 0;
        foreach (var chunk in chunks)
        {
            processedChunks++;
            var chunkPercent = 62 + (int)Math.Round(processedChunks * 28d / chunkCount);
            progress?.Report(
                "Graph seed",
                $"Recording graph chunk {processedChunks:N0} of {chunks.Count:N0} for lazy enrichment.",
                chunkPercent);

            var chunkNode = new GraphNode(
                chunk.Id.ToString(),
                $"{sourceName} chunk {chunk.Index}",
                "Chunk",
                new Dictionary<string, object>
                {
                    ["sourceId"] = sourceId.ToString(),
                    ["sourceName"] = sourceName,
                    ["chunkIndex"] = chunk.Index,
                    ["content"] = chunk.Content,
                    ["lazyEnriched"] = false,
                    ["lazyEnrichmentStatus"] = "Pending"
                });

            await _graphProvider.CreateNodeAsync(chunkNode, cancellationToken).ConfigureAwait(false);
            await _graphProvider.CreateRelationshipAsync(
                new GraphEdge(
                    $"{sourceId}-has_chunk-{chunk.Id}",
                    sourceId.ToString(),
                    chunk.Id.ToString(),
                    "has_chunk",
                    new Dictionary<string, object>
                    {
                        ["sourceId"] = sourceId.ToString(),
                        ["chunkIndex"] = chunk.Index,
                        ["summary"] = $"{sourceName} contains chunk {chunk.Index}."
                    }),
                cancellationToken).ConfigureAwait(false);
        }

        progress?.Report("Lazy enrichment ready", "Document is searchable; graph enrichment will run on relevant queries.", 94, force: true);
    }

    private async Task PersistGraphIntelligenceAsync(
        Guid sourceId,
        string sourceName,
        string content,
        IIngestionProgressSink? progress,
        CancellationToken cancellationToken)
    {
        var sourceNode = new GraphNode(
            sourceId.ToString(),
            sourceName,
            "Source",
            new Dictionary<string, object>
            {
                ["sourceId"] = sourceId.ToString(),
                ["sourceName"] = sourceName,
                ["content"] = content
            });

        await _graphProvider.CreateNodeAsync(sourceNode, cancellationToken).ConfigureAwait(false);
        progress?.Report("Source summary", "Summarizing the source document node.", 72, force: true);
        await PersistNodeSummaryAsync(sourceNode, cancellationToken).ConfigureAwait(false);

        var summarizedEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chunks = _chunkingPipeline.Chunk(sourceId, content).Take(MaxGraphChunks).ToList();
        var chunkCount = Math.Max(chunks.Count, 1);
        var processedChunks = 0;
        foreach (var chunk in chunks)
        {
            processedChunks++;
            var chunkPercent = 72 + (int)Math.Round(processedChunks * 18d / chunkCount);
            progress?.Report(
                "Chunk graph enrichment",
                $"Processing graph chunk {processedChunks:N0} of {chunks.Count:N0}.",
                chunkPercent);

            var chunkNode = new GraphNode(
                chunk.Id.ToString(),
                $"{sourceName} chunk {chunk.Index}",
                "Chunk",
                new Dictionary<string, object>
                {
                    ["sourceId"] = sourceId.ToString(),
                    ["sourceName"] = sourceName,
                    ["chunkIndex"] = chunk.Index,
                    ["content"] = chunk.Content
                });

            await _graphProvider.CreateNodeAsync(chunkNode, cancellationToken).ConfigureAwait(false);
            await _graphProvider.CreateRelationshipAsync(
                new GraphEdge(
                    $"{sourceId}-has_chunk-{chunk.Id}",
                    sourceId.ToString(),
                    chunk.Id.ToString(),
                    "has_chunk",
                    new Dictionary<string, object>
                    {
                        ["sourceId"] = sourceId.ToString(),
                        ["chunkIndex"] = chunk.Index,
                        ["summary"] = $"{sourceName} contains chunk {chunk.Index}."
                    }),
                cancellationToken).ConfigureAwait(false);

            var chunkEntities = await DiscoverChunkEntitiesAsync(chunk.Content, cancellationToken).ConfigureAwait(false);
            foreach (var entity in chunkEntities)
            {
                var entityNode = new GraphNode(
                    entity.Id,
                    entity.Name,
                    entity.Type,
                    new Dictionary<string, object>
                    {
                        ["confidence"] = entity.Confidence,
                        ["description"] = entity.Description ?? string.Empty,
                        ["sourceId"] = sourceId.ToString(),
                        ["sourceName"] = sourceName,
                        ["chunkId"] = chunk.Id.ToString(),
                        ["chunkIndex"] = chunk.Index
                    });

                await _graphProvider.CreateNodeAsync(entityNode, cancellationToken).ConfigureAwait(false);
                await _graphProvider.CreateRelationshipAsync(
                    new GraphEdge(
                        $"{entity.Id}-source-{sourceId}",
                        entity.Id,
                        sourceId.ToString(),
                        "found_in",
                        new Dictionary<string, object>
                        {
                            ["sourceId"] = sourceId.ToString(),
                            ["sourceName"] = sourceName,
                            ["summary"] = $"{entity.Name} was found in {sourceName}."
                        }),
                    cancellationToken).ConfigureAwait(false);
                await _graphProvider.CreateRelationshipAsync(
                    new GraphEdge(
                        $"{entity.Id}-mentioned_in-{chunk.Id}",
                        entity.Id,
                        chunk.Id.ToString(),
                        "mentioned_in",
                        new Dictionary<string, object>
                        {
                            ["sourceId"] = sourceId.ToString(),
                            ["chunkIndex"] = chunk.Index,
                            ["summary"] = $"{entity.Name} is mentioned in chunk {chunk.Index}."
                        }),
                    cancellationToken).ConfigureAwait(false);

                if (summarizedEntities.Add(entity.Id))
                {
                    await PersistNodeSummaryAsync(entityNode, cancellationToken).ConfigureAwait(false);
                }
            }

            var relationships = await _relationshipExtraction
                .DiscoverAsync(chunk.Content, chunkEntities, cancellationToken)
                .ConfigureAwait(false);

            if (relationships.IsFailure || relationships.Value is null)
            {
                continue;
            }

            foreach (var relationship in relationships.Value)
            {
                var sourceEntity = chunkEntities.FirstOrDefault(e => e.Id.Equals(relationship.SourceId, StringComparison.OrdinalIgnoreCase));
                var targetEntity = chunkEntities.FirstOrDefault(e => e.Id.Equals(relationship.TargetId, StringComparison.OrdinalIgnoreCase));
                await _graphProvider.CreateRelationshipAsync(
                    new GraphEdge(
                        StableRelationshipId(relationship.SourceId, relationship.TargetId, relationship.Type, chunk.Id.ToString()),
                        relationship.SourceId,
                        relationship.TargetId,
                        NormalizeRelationshipType(relationship.Type),
                        new Dictionary<string, object>
                        {
                            ["confidence"] = relationship.Confidence,
                            ["description"] = relationship.Description ?? string.Empty,
                            ["summary"] = BuildRelationshipSummary(relationship, sourceEntity, targetEntity, chunk.Index),
                            ["sourceId"] = sourceId.ToString(),
                            ["sourceName"] = sourceName,
                            ["chunkId"] = chunk.Id.ToString(),
                            ["chunkIndex"] = chunk.Index
                        }),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        progress?.Report("Community detection", "Detecting graph communities.", 92, force: true);
        var communities = await _communityDetection.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (communities.IsFailure || communities.Value is null)
        {
            return;
        }

        var communityCount = Math.Max(communities.Value.Take(50).Count(), 1);
        var processedCommunities = 0;
        foreach (var community in communities.Value.Take(50))
        {
            processedCommunities++;
            var communityPercent = 92 + (int)Math.Round(processedCommunities * 6d / communityCount);
            progress?.Report(
                "Community summaries",
                $"Summarizing community {processedCommunities:N0} of {communityCount:N0}.",
                communityPercent);

            var summary = await _graphSummary.SummarizeCommunityAsync(community.Id, cancellationToken).ConfigureAwait(false);
            if (summary.IsFailure || string.IsNullOrWhiteSpace(summary.Value))
            {
                continue;
            }

            await _graphProvider.UpdateNodeAsync(
                new GraphNode(
                    community.Id,
                    community.Name,
                    "Community",
                    new Dictionary<string, object>
                    {
                        ["description"] = community.Description ?? string.Empty,
                        ["summary"] = summary.Value,
                        ["algorithm"] = community.Metadata.TryGetValue("algorithm", out var algorithm) ? algorithm : "leiden",
                        ["level"] = community.Metadata.TryGetValue("level", out var level) ? level : 0,
                        ["memberCount"] = community.MemberIds.Count
                    }),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<ExtractedEntity>> DiscoverChunkEntitiesAsync(string content, CancellationToken cancellationToken)
    {
        var entities = await DiscoverEntitiesAsync(content, cancellationToken).ConfigureAwait(false);
        return entities
            .Select(e => new ExtractedEntity
            {
                Id = StableId("entity", e.Type, e.Name),
                Name = e.Name,
                Type = string.IsNullOrWhiteSpace(e.Type) ? "Entity" : e.Type,
                Description = e.Description,
                Confidence = e.Confidence,
                Properties = e.Properties
            })
            .Take(MaxEntities)
            .ToList();
    }

    private async Task PersistNodeSummaryAsync(GraphNode node, CancellationToken cancellationToken)
    {
        var summary = await _graphSummary.SummarizeEntityAsync(node.Id, cancellationToken).ConfigureAwait(false);
        if (summary.IsFailure || string.IsNullOrWhiteSpace(summary.Value))
        {
            return;
        }

        var properties = new Dictionary<string, object>(node.Properties)
        {
            ["summary"] = summary.Value
        };

        await _graphProvider.UpdateNodeAsync(
            new GraphNode(node.Id, node.Label, node.Type, properties),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> DeleteSourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Source ID is required.", nameof(sourceId));
        }

        const string sql = @"
            WITH deleted_tag_links AS (
                DELETE FROM taxonomy_tag_sources
                WHERE source_id = CAST(@SourceId AS uuid)
                RETURNING tag_id
            ),
            deleted_tags AS (
                DELETE FROM taxonomy_tags t
                WHERE t.id IN (SELECT tag_id FROM deleted_tag_links)
                  AND NOT EXISTS (
                      SELECT 1
                      FROM taxonomy_tag_sources s
                      WHERE s.tag_id = t.id
                  )
                RETURNING t.category_id
            )
            DELETE FROM categories c
            WHERE c.id IN (SELECT category_id FROM deleted_tags)
              AND c.name IN ('Topics', 'Entities')
              AND NOT EXISTS (
                  SELECT 1
                  FROM taxonomy_tags t
                  WHERE t.category_id = c.id
              );

            DELETE FROM ontology_relationships
            WHERE properties ->> 'sourceId' = @SourceId;

            DELETE FROM ontology_entities
            WHERE entity_type = 'SourceDocument'
              AND properties ->> 'sourceId' = @SourceId;

            DELETE FROM ontology_entities e
            WHERE properties ->> 'lastSourceId' = @SourceId
              AND NOT EXISTS (
                  SELECT 1
                  FROM ontology_relationships r
                  WHERE r.source_entity_id = e.id OR r.target_entity_id = e.id
              );";

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
            await connection.ExecuteAsync(sql, new { SourceId = sourceId.ToString() }).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Knowledge source delete failed. {ex.Message}");
        }
    }

        private async Task<IReadOnlyList<ExtractedEntity>> DiscoverEntitiesAsync(string content, CancellationToken cancellationToken)
    {
        var result = await _entityExtraction.DiscoverAsync(content, null, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess && result.Value is not null && result.Value.Count > 0)
        {
            var normalized = result.Value
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .Select(e => new { Original = e, Normalized = _termNormalizer.Normalize(e.Name) })
                .Where(x => x.Normalized != null)
                .GroupBy(x => x.Normalized, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var first = g.First().Original;
                    return new ExtractedEntity
                    {
                        Id = first.Id,
                        Name = g.Key,
                        Type = first.Type,
                        Description = first.Description,
                        Confidence = first.Confidence,
                        Properties = first.Properties
                    };
                })
                .Take(MaxEntities)
                .ToList();

            return normalized;
        }

        var topics = ExtractTopics(content, Array.Empty<string>());
        return topics.Take(MaxEntities)
            .Select(topic => new ExtractedEntity
            {
                Name = topic,
                Type = "Topic",
                Confidence = 0.45
            })
            .ToList();
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            CREATE EXTENSION IF NOT EXISTS pgcrypto;

            CREATE TABLE IF NOT EXISTS categories (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                name TEXT NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS taxonomy_tags (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                category_id UUID NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                UNIQUE(category_id, name)
            );

            CREATE TABLE IF NOT EXISTS ontology_entities (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                name TEXT NOT NULL UNIQUE,
                entity_type TEXT NOT NULL,
                properties JSONB NOT NULL DEFAULT '{}'::jsonb
            );

            CREATE TABLE IF NOT EXISTS ontology_relationships (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                source_entity_id UUID NOT NULL REFERENCES ontology_entities(id) ON DELETE CASCADE,
                target_entity_id UUID NOT NULL REFERENCES ontology_entities(id) ON DELETE CASCADE,
                relationship_type TEXT NOT NULL,
                properties JSONB NOT NULL DEFAULT '{}'::jsonb,
                UNIQUE(source_entity_id, target_entity_id, relationship_type)
            );

            CREATE TABLE IF NOT EXISTS taxonomy_tag_sources (
                tag_id UUID NOT NULL REFERENCES taxonomy_tags(id) ON DELETE CASCADE,
                source_id UUID NOT NULL,
                source_name TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                PRIMARY KEY(tag_id, source_id)
            );";

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(sql).ConfigureAwait(false);
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

    private static IEnumerable<(Guid SourceId, Guid TargetId)> BuildEntityPairs(IReadOnlyList<Guid> entityIds)
    {
        for (var i = 0; i < entityIds.Count; i++)
        {
            for (var j = i + 1; j < entityIds.Count; j++)
            {
                yield return (entityIds[i], entityIds[j]);
            }
        }
    }

    

    private IEnumerable<string> ExtractTopics(string text, IEnumerable<string> entityNames)
    {
        // Use regex to find candidate words (minimum 3 characters) and normalize them via the configured term normalizer.
        var rawTokens = WordPattern.Matches(text).Select(m => m.Value);
        var normalized = rawTokens
            .Select(t => _termNormalizer.Normalize(t))
            .Where(t => t != null)
            .Except(entityNames, StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTopics)
            .ToList();
        return normalized;
    }

    private static string GetSourceEntityName(Guid sourceId, string sourceName)
    {
        return string.IsNullOrWhiteSpace(sourceName)
            ? $"Source:{sourceId}"
            : $"Source:{sourceId}:{sourceName}";
    }

    private static string StableRelationshipId(string sourceId, string targetId, string relationshipType, string chunkId)
    {
        return StableId("relationship", sourceId, targetId, NormalizeRelationshipType(relationshipType), chunkId);
    }

    private static string StableId(string prefix, params string[] parts)
    {
        var key = string.Join("|", parts.Select(p => p.Trim().ToLowerInvariant()));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return $"{prefix}:{Convert.ToHexString(bytes)[..16].ToLowerInvariant()}";
    }

    private static string NormalizeRelationshipType(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return "related_to";
        }

        var normalized = Regex.Replace(type.Trim().ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "related_to" : normalized;
    }

    private static string BuildRelationshipSummary(
        ExtractedRelationship relationship,
        ExtractedEntity? sourceEntity,
        ExtractedEntity? targetEntity,
        int chunkIndex)
    {
        if (!string.IsNullOrWhiteSpace(relationship.Description))
        {
            return relationship.Description;
        }

        var source = sourceEntity?.Name ?? relationship.SourceId;
        var target = targetEntity?.Name ?? relationship.TargetId;
        return $"{source} {NormalizeRelationshipType(relationship.Type).Replace('_', ' ')} {target} in chunk {chunkIndex}.";
    }
}








