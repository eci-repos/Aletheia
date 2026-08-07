using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Aletheia.RAGS.Application.Pipelines;
using Aletheia.Repository.API.Services;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;

namespace RAGS.UnitTests;

public sealed class UploadedContentKnowledgeIndexerTests
{
    [Fact]
    public async Task IndexLightweightAsync_links_rfp_taxonomy_and_ontology_to_each_source_when_postgres_is_available()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSQL")
            ?? "Host=localhost;Port=5432;Database=aletheia;Username=aletheia;Password=aletheia";

        PostgreSqlConnectionFactory factory;
        try
        {
            factory = new PostgreSqlConnectionFactory(connectionString);
            await using var probe = factory.CreateConnection();
            await probe.OpenAsync();
        }
        catch
        {
            return;
        }

        var sourceA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();
        var indexer = new UploadedContentKnowledgeIndexer(
            factory,
            new EmptyEntityExtractionService(),
            new EmptyRelationshipExtractionService(),
            new MemoryGraphProvider(),
            new EmptyGraphSummaryService(),
            new EmptyCommunityDetectionService(),
            new ChunkingPipeline(),
            new ConfigurableTermNormalizer(Options.Create(new TaxonomyOptions()), NullLogger<ConfigurableTermNormalizer>.Instance));

        var resultA = await indexer.IndexLightweightAsync(
            sourceA,
            "CMP 2026 - 3. RFP Analysis.docx",
            "The request for proposal covers AI features and implementation services.",
            null);
        var resultB = await indexer.IndexLightweightAsync(
            sourceB,
            "CMP 2022 - 3. RFP Analysis.docx",
            "This RFP analysis describes managed analytics services.",
            null);

        Assert.True(resultA.IsSuccess, resultA.Error);
        Assert.True(resultB.IsSuccess, resultB.Error);

        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        try
        {
            const string taxonomySql = @"
                SELECT COUNT(DISTINCT ts.source_id)
                FROM taxonomy_tags t
                JOIN taxonomy_tag_sources ts ON ts.tag_id = t.id
                WHERE t.name = 'rfp'
                  AND ts.source_id = ANY(@SourceIds);";
            var taxonomyCount = await connection.ExecuteScalarAsync<int>(taxonomySql, new { SourceIds = new[] { sourceA, sourceB } });
            Assert.Equal(2, taxonomyCount);

            const string ontologySql = @"
                SELECT COUNT(DISTINCT target.name)
                FROM ontology_entities source
                JOIN ontology_relationships r ON r.source_entity_id = source.id
                JOIN ontology_entities target ON target.id = r.target_entity_id
                WHERE source.name = 'rfp'
                  AND r.relationship_type = 'found_in'
                  AND target.properties ->> 'sourceId' = ANY(@SourceIds);";
            var ontologyCount = await connection.ExecuteScalarAsync<int>(ontologySql, new { SourceIds = new[] { sourceA.ToString(), sourceB.ToString() } });
            Assert.Equal(2, ontologyCount);
        }
        finally
        {
            await indexer.DeleteSourceAsync(sourceA);
            await indexer.DeleteSourceAsync(sourceB);
        }
    }

    private sealed class EmptyEntityExtractionService : IEntityExtractionService
    {
        public Task<Result<IReadOnlyList<ExtractedEntity>>> DiscoverAsync(string text, IGraphTraversalBudget? budget = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<ExtractedEntity>>.Success(Array.Empty<ExtractedEntity>()));
        }

        public Task<Result<IReadOnlyList<ExtractedEntity>>> ClassifyAsync(IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<ExtractedEntity>>.Success(entities));
        }

        public Task<Result<IReadOnlyList<ExtractedEntity>>> ScoreConfidenceAsync(IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<ExtractedEntity>>.Success(entities));
        }
    }

    private sealed class EmptyRelationshipExtractionService : IRelationshipExtractionService
    {
        public Task<Result<IReadOnlyList<ExtractedRelationship>>> DiscoverAsync(string text, IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<ExtractedRelationship>>.Success(Array.Empty<ExtractedRelationship>()));
        }

        public Task<Result<IReadOnlyList<ExtractedRelationship>>> ClassifyAsync(IReadOnlyList<ExtractedRelationship> relationships, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<ExtractedRelationship>>.Success(relationships));
        }

        public Task<Result<IReadOnlyList<ExtractedRelationship>>> ScoreConfidenceAsync(IReadOnlyList<ExtractedRelationship> relationships, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<ExtractedRelationship>>.Success(relationships));
        }
    }

    private sealed class EmptyGraphSummaryService : IGraphSummaryService
    {
        public Task<Result<string>> SummarizeEntityAsync(string entityId, CancellationToken cancellationToken = default) => Task.FromResult(Result<string>.Success(string.Empty));

        public Task<Result<string>> SummarizeCommunityAsync(string communityId, CancellationToken cancellationToken = default) => Task.FromResult(Result<string>.Success(string.Empty));

        public Task<Result<string>> SummarizeClusterAsync(string clusterId, CancellationToken cancellationToken = default) => Task.FromResult(Result<string>.Success(string.Empty));

        public Task<Result<string>> SummarizeGlobalAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<string>.Success(string.Empty));
    }

    private sealed class EmptyCommunityDetectionService : ICommunityDetectionService
    {
        public Task<Result<IReadOnlyList<GraphCommunity>>> DiscoverAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<IReadOnlyList<GraphCommunity>>.Success(Array.Empty<GraphCommunity>()));

        public Task<Result<IReadOnlyList<GraphCommunity>>> DetectClustersAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<IReadOnlyList<GraphCommunity>>.Success(Array.Empty<GraphCommunity>()));

        public Task<Result> AssignAsync(string nodeId, string communityId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());

        public Task<Result<GraphCommunity?>> GetCommunityAsync(string communityId, CancellationToken cancellationToken = default) => Task.FromResult(Result<GraphCommunity?>.Success(null));

        public Task<Result<IReadOnlyList<GraphCommunity>>> GetCommunitiesForNodeAsync(string nodeId, CancellationToken cancellationToken = default) => Task.FromResult(Result<IReadOnlyList<GraphCommunity>>.Success(Array.Empty<GraphCommunity>()));
    }

    private sealed class MemoryGraphProvider : IGraphProvider
    {
        private readonly Dictionary<string, GraphNode> _nodes = new();
        private readonly Dictionary<string, GraphEdge> _edges = new();

        public Task<Result<GraphNode?>> GetNodeAsync(string id, CancellationToken cancellationToken = default)
        {
            _nodes.TryGetValue(id, out var node);
            return Task.FromResult(Result<GraphNode?>.Success(node));
        }

        public Task<Result> CreateNodeAsync(GraphNode node, CancellationToken cancellationToken = default)
        {
            _nodes[node.Id] = node;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> UpdateNodeAsync(GraphNode node, CancellationToken cancellationToken = default)
        {
            _nodes[node.Id] = node;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteNodeAsync(string id, CancellationToken cancellationToken = default)
        {
            _nodes.Remove(id);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<GraphEdge?>> GetRelationshipAsync(string id, CancellationToken cancellationToken = default)
        {
            _edges.TryGetValue(id, out var edge);
            return Task.FromResult(Result<GraphEdge?>.Success(edge));
        }

        public Task<Result> CreateRelationshipAsync(GraphEdge edge, CancellationToken cancellationToken = default)
        {
            _edges[edge.Id] = edge;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteRelationshipAsync(string id, CancellationToken cancellationToken = default)
        {
            _edges.Remove(id);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<GraphNode>>> GetNodesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(_nodes.Values.ToList()));

        public Task<Result<IReadOnlyList<GraphEdge>>> GetEdgesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<IReadOnlyList<GraphEdge>>.Success(_edges.Values.ToList()));

        public Task<Result<IReadOnlyList<GraphNode>>> GetNeighborsAsync(string nodeId, CancellationToken cancellationToken = default) => Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(Array.Empty<GraphNode>()));

        public Task<Result<IReadOnlyList<GraphNode>>> SearchNodesAsync(string label, CancellationToken cancellationToken = default) => Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(_nodes.Values.Where(node => node.Label.Contains(label, StringComparison.OrdinalIgnoreCase)).ToList()));

        public Task<Result<IReadOnlyList<GraphEdge>>> SearchRelationshipsAsync(string type, CancellationToken cancellationToken = default) => Task.FromResult(Result<IReadOnlyList<GraphEdge>>.Success(_edges.Values.Where(edge => edge.RelationshipType.Equals(type, StringComparison.OrdinalIgnoreCase)).ToList()));

        public Task<Result<IReadOnlyList<GraphPath>>> FindPathsAsync(string startNodeId, string endNodeId, CancellationToken cancellationToken = default) => Task.FromResult(Result<IReadOnlyList<GraphPath>>.Success(Array.Empty<GraphPath>()));

        public Task<Result<IReadOnlyList<GraphNode>>> GetSubgraphAsync(string nodeId, int depth, CancellationToken cancellationToken = default) => Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(Array.Empty<GraphNode>()));

        public Task<Result<bool>> GraphExistsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<bool>.Success(true));

        public Task<Result> ClearAsync(CancellationToken cancellationToken = default)
        {
            _nodes.Clear();
            _edges.Clear();
            return Task.FromResult(Result.Success());
        }
    }
}
