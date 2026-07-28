using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Interfaces;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;
using Npgsql;

namespace Aletheia.KnowledgeGraph.Infrastructure.Neo4j.GraphStore;

public sealed class GraphSyncService
{
    private const string SyncFailedMessage = "Graph sync failed.";

    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly IGraphService _graphService;

    public GraphSyncService(PostgreSqlConnectionFactory connectionFactory, IGraphService graphService)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _graphService = graphService ?? throw new ArgumentNullException(nameof(graphService));
    }

    public async Task<Result> SyncFromOntologyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Clear existing graph
            var clearResult = await _graphService.ClearAsync(cancellationToken).ConfigureAwait(false);
            if (clearResult.IsFailure)
            {
                return Result.Failure(clearResult.Error ?? "Failed to clear graph.");
            }

            // Read entities from PostgreSQL
            var entities = await GetOntologyEntitiesAsync(cancellationToken).ConfigureAwait(false);
            if (entities.IsFailure)
            {
                return Result.Failure(entities.Error ?? "Failed to read entities.");
            }

            // Create nodes in Neo4j
            foreach (var entity in entities.Value!)
            {
                var node = new GraphNode(
                    entity.Id.ToString(),
                    entity.Label,
                    entity.EntityType);

                var nodeResult = await _graphService.CreateNodeAsync(node, cancellationToken).ConfigureAwait(false);
                if (nodeResult.IsFailure)
                {
                    return Result.Failure(nodeResult.Error ?? "Failed to create node.");
                }
            }

            // Read relationships from PostgreSQL
            var relationships = await GetOntologyRelationshipsAsync(cancellationToken).ConfigureAwait(false);
            if (relationships.IsFailure)
            {
                return Result.Failure(relationships.Error ?? "Failed to read relationships.");
            }

            // Create edges in Neo4j
            foreach (var rel in relationships.Value!)
            {
                var edge = new GraphEdge(
                    $"{rel.SourceId}-{rel.TargetId}-{rel.RelationshipType}",
                    rel.SourceId.ToString(),
                    rel.TargetId.ToString(),
                    rel.RelationshipType);

                var edgeResult = await _graphService.CreateEdgeAsync(edge, cancellationToken).ConfigureAwait(false);
                if (edgeResult.IsFailure)
                {
                    return Result.Failure(edgeResult.Error ?? "Failed to create edge.");
                }
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"{SyncFailedMessage} {ex.Message}");
        }
    }

    private async Task<Result<IReadOnlyList<OntologyEntityRow>>> GetOntologyEntitiesAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT
                id AS ""Id"",
                CASE
                    WHEN entity_type = 'SourceDocument'
                        THEN COALESCE(NULLIF(properties ->> 'sourceName', ''), regexp_replace(name, '^Source:[^:]+:', ''))
                    ELSE name
                END AS ""Label"",
                entity_type AS ""EntityType""
            FROM ontology_entities
            ORDER BY name";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await connection.QueryAsync<OntologyEntityRow>(sql).ConfigureAwait(false);
            return Result<IReadOnlyList<OntologyEntityRow>>.Success(rows.ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<OntologyEntityRow>>.Failure(ex.Message);
        }
    }

    private async Task<Result<IReadOnlyList<OntologyRelationshipRow>>> GetOntologyRelationshipsAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT
                s.id AS ""SourceId"",
                t.id AS ""TargetId"",
                r.relationship_type AS ""RelationshipType""
            FROM ontology_relationships r
            JOIN ontology_entities s ON s.id = r.source_entity_id
            JOIN ontology_entities t ON t.id = r.target_entity_id";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await connection.QueryAsync<OntologyRelationshipRow>(sql).ConfigureAwait(false);
            return Result<IReadOnlyList<OntologyRelationshipRow>>.Success(rows.ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<OntologyRelationshipRow>>.Failure(ex.Message);
        }
    }

    private record OntologyEntityRow
    {
        public Guid Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
    }

    private record OntologyRelationshipRow
    {
        public Guid SourceId { get; set; }
        public Guid TargetId { get; set; }
        public string RelationshipType { get; set; } = string.Empty;
    }
}
