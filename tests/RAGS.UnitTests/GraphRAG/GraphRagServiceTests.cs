using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.GraphRAG;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace RAGS.UnitTests.GraphRAG;

public sealed class GraphRagServiceTests
{
    [Fact]
    public async Task IngestAsync_stores_chunks_and_creates_graph_nodes()
    {
        var ragsService = new MockRagsService();
        var graphProvider = new MockGraphProvider();
        var service = CreateService(ragsService, graphProvider);
        var sourceId = Guid.NewGuid();

        var result = await service.IngestAsync(new IngestionRequest(sourceId, new string('a', 3000)));

        Assert.True(result.IsSuccess);
        Assert.True(ragsService.Ingested);
        Assert.True(graphProvider.NodesCreated.Count > 0);
    }

    [Fact]
    public async Task IngestAsync_extracts_graph_intelligence_per_chunk()
    {
        var ragsService = new MockRagsService();
        var graphProvider = new MockGraphProvider();
        var service = CreateService(
            ragsService,
            graphProvider,
            entityExtraction: new ChunkEntityExtractionService(),
            relationshipExtraction: new ChunkRelationshipExtractionService());
        var sourceId = Guid.NewGuid();
        var content = $"{new string('a', 1100)} Alpha works with Beta. {new string('b', 1100)} Alpha works with Beta.";

        var result = await service.IngestAsync(new IngestionRequest(sourceId, content, "source.txt"));

        Assert.True(result.IsSuccess);
        Assert.Contains(graphProvider.NodesCreated, n => n.Type == "Chunk");
        Assert.Contains(graphProvider.NodesCreated, n => n.Type == "Person" && n.Label == "Alpha");
        Assert.Contains(graphProvider.NodesCreated, n => n.Type == "Person" && n.Label == "Beta");
        Assert.Contains(graphProvider.EdgesCreated, e => e.RelationshipType == "has_chunk");
        Assert.Contains(graphProvider.EdgesCreated, e => e.RelationshipType == "mentioned_in");
        Assert.Contains(graphProvider.EdgesCreated, e =>
            e.RelationshipType == "works_with" &&
            e.Properties.TryGetValue("summary", out var summary) &&
            summary.ToString()!.Contains("works with", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(graphProvider.NodesCreated, n =>
            n.Label == "Alpha" &&
            n.Properties.TryGetValue("summary", out var summary) &&
            summary.ToString()!.Contains("Summary of", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IngestAsync_fails_when_rags_service_fails()
    {
        var ragsService = new FailingRagsService();
        var graphProvider = new MockGraphProvider();
        var service = CreateService(ragsService, graphProvider);

        var result = await service.IngestAsync(new IngestionRequest(Guid.NewGuid(), "test content"));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task RetrieveAsync_returns_results_when_no_neighbors()
    {
        var ragsService = new MockRagsService();
        var graphProvider = new MockGraphProvider();
        var service = CreateService(ragsService, graphProvider);

        var result = await service.RetrieveAsync("query", topK: 2);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Count);
        Assert.All(result.Value, item =>
        {
            Assert.True(item.Rank > 0);
            Assert.NotEmpty(item.Citations);
            Assert.NotEmpty(item.RankingSignals);
            Assert.Contains("final", item.RankingSignals.Keys);
            Assert.Equal("graph-aware", item.RetrievalStrategy);
        });
    }

    [Fact]
    public async Task RetrieveAsync_prefers_stored_summaries_over_raw_chunk_search()
    {
        var ragsService = new FailingRagsRetrieveService();
        var graphProvider = new MockGraphProvider();
        var sourceId = Guid.NewGuid();
        await graphProvider.CreateNodeAsync(new GraphNode(
            "entity-alpha",
            "Alpha",
            "Person",
            new Dictionary<string, object>
            {
                ["sourceId"] = sourceId.ToString(),
                ["summary"] = "Alpha coordinates the launch workstream."
            }));
        var service = CreateService(
            ragsService,
            graphProvider,
            graphSummary: new StoredSummaryGraphSummaryService(graphProvider));

        var result = await service.RetrieveAsync("Alpha", topK: 1);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Equal("summary-entity", item.RetrievalStrategy);
        Assert.Contains("Entity Summary: Alpha", item.Chunk.Content);
        Assert.Contains("Alpha coordinates the launch workstream.", item.Chunk.Content);
    }

    [Fact]
    public async Task RetrieveAsync_syncs_lazy_entities_to_taxonomy_and_ontology_sink()
    {
        var sink = new RecordingLazyEnrichmentKnowledgeSink();
        var service = CreateService(
            new MockRagsService(),
            new MockGraphProvider(),
            entityExtraction: new ChunkEntityExtractionService(),
            relationshipExtraction: new ChunkRelationshipExtractionService(),
            knowledgeSink: sink);

        var result = await service.RetrieveAsync("Alpha works with Beta", topK: 1);

        Assert.True(result.IsSuccess);
        Assert.True(sink.Calls > 0);
        Assert.Contains(sink.Entities, entity => entity.Name == "Alpha");
        Assert.Contains(sink.Entities, entity => entity.Name == "Beta");
        Assert.Contains(sink.Relationships, relationship => relationship.Type == "works_with");
    }

    [Fact]
    public async Task GlobalSearchAsync_uses_top_level_community_summaries_for_map_reduce()
    {
        var service = new GlobalGraphSearchService(
            new TopLevelCommunityDetectionService(),
            new MockGraphSummaryService(),
            new MockHierarchicalSummaryService(),
            new MockGraphContextBuilder(),
            new MockCitationPathService());

        var result = await service.SearchAsync("What are the main themes?");

        Assert.True(result.IsSuccess);
        Assert.Contains("Reduce Step", result.Value!.Answer);
        Assert.Contains("Community: Top Community", result.Value.Answer);
        Assert.DoesNotContain("Community: Lower Community", result.Value.Answer);
    }

    [Fact]
    public async Task RetrieveAsync_expands_with_neighbors()
    {
        var ragsService = new MockRagsService();
        var graphProvider = new MockGraphProvider();
        var service = CreateService(ragsService, graphProvider);

        var result = await service.RetrieveAsync("query", topK: 2, maxExpanded: 6);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Count >= 2); // at least original results
    }

    [Fact]
    public async Task RetrieveAsync_returns_failure_when_rags_retrieval_fails()
    {
        var ragsService = new FailingRagsRetrieveService();
        var graphProvider = new MockGraphProvider();
        var service = CreateService(ragsService, graphProvider);

        var result = await service.RetrieveAsync("query");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task IngestAsync_does_not_persist_keyword_noise_entities()
    {
        var ragsService = new MockRagsService();
        var graphProvider = new MockGraphProvider();
        var service = CreateService(
            ragsService,
            graphProvider,
            entityExtraction: new NoiseEntityExtractionService());

        var result = await service.IngestAsync(new IngestionRequest(
            Guid.NewGuid(),
            $"{new string('a', 1100)} alpha Bravo. {new string('b', 1100)}"));

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(graphProvider.NodesCreated, n =>
            n.Type.Equals("keyword", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(graphProvider.NodesCreated, n =>
            n.Type.Equals("project", StringComparison.OrdinalIgnoreCase) && n.Label == "Bravo");
    }

    [Fact]
    public async Task RetrieveAsync_populates_retrieval_trace()
    {
        var ragsService = new MockRagsService();
        var graphProvider = new MockGraphProvider();
        var service = CreateService(ragsService, graphProvider);

        var result = await service.RetrieveAsync("query", topK: 2);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value!);
        var first = result.Value!.First();
        Assert.NotNull(first.Trace);
        Assert.False(string.IsNullOrEmpty(first.Trace!.Strategy));
        Assert.NotEmpty(first.Trace.Steps);
        Assert.True(first.Trace.ElapsedMs >= 0);
        Assert.Contains("entity-resolution", first.Trace.Steps);
    }

    private static GraphRagService CreateService(
        IRagsService ragsService,
        IGraphProvider graphProvider,
        IEntityExtractionService? entityExtraction = null,
        IRelationshipExtractionService? relationshipExtraction = null,
        IGraphSummaryService? graphSummary = null,
        ILazyEnrichmentKnowledgeSink? knowledgeSink = null)
    {
        return new GraphRagService(
            ragsService,
            graphProvider,
            entityExtraction ?? new MockEntityExtractionService(),
            relationshipExtraction ?? new MockRelationshipExtractionService(),
            new MockGraphReasoningService(ragsService, graphProvider),
            graphSummary ?? new MockGraphSummaryService(),
            new MockHierarchicalSummaryService(),
            new MockCommunityDetectionService(graphProvider),
            new MockGraphContextBuilder(),
            new MockCitationPathService(),
            new MockGlobalGraphSearchService(),
            knowledgeSink);
    }

    private sealed class ChunkEntityExtractionService : IEntityExtractionService
    {
        public Task<Result<IReadOnlyList<ExtractedEntity>>> DiscoverAsync(string text, IGraphTraversalBudget? budget = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ExtractedEntity> entities = new[]
            {
                new ExtractedEntity { Name = "Alpha", Type = "Person", Confidence = 0.9 },
                new ExtractedEntity { Name = "Beta", Type = "Person", Confidence = 0.85 }
            };

            return Task.FromResult(Result<IReadOnlyList<ExtractedEntity>>.Success(entities));
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

    private sealed class ChunkRelationshipExtractionService : IRelationshipExtractionService
    {
        public Task<Result<IReadOnlyList<ExtractedRelationship>>> DiscoverAsync(string text, IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default)
        {
            var source = entities.First(e => e.Name == "Alpha");
            var target = entities.First(e => e.Name == "Beta");
            IReadOnlyList<ExtractedRelationship> relationships = new[]
            {
                new ExtractedRelationship
                {
                    SourceId = source.Id,
                    TargetId = target.Id,
                    Type = "works_with",
                    Description = "Alpha works with Beta.",
                    Confidence = 0.8
                }
            };

            return Task.FromResult(Result<IReadOnlyList<ExtractedRelationship>>.Success(relationships));
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

    private sealed class MockEntityExtractionService : IEntityExtractionService
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

    private sealed class NoiseEntityExtractionService : IEntityExtractionService
    {
        public Task<Result<IReadOnlyList<ExtractedEntity>>> DiscoverAsync(string text, IGraphTraversalBudget? budget = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ExtractedEntity> entities = new[]
            {
                new ExtractedEntity { Name = "alpha", Type = "keyword", Confidence = 0.5 },
                new ExtractedEntity { Name = "Bravo", Type = "project", Confidence = 0.9 }
            };

            return Task.FromResult(Result<IReadOnlyList<ExtractedEntity>>.Success(entities));
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

    private sealed class MockRelationshipExtractionService : IRelationshipExtractionService
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

    private sealed class MockGraphReasoningService : IGraphReasoningService
    {
        private readonly IRagsService _ragsService;
        private readonly IGraphProvider _provider;

        public MockGraphReasoningService(IRagsService ragsService, IGraphProvider provider)
        {
            _ragsService = ragsService;
            _provider = provider;
        }

        public Task<Result<IReadOnlyList<GraphPath>>> DiscoverReasoningPathsAsync(string query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphPath>>.Success(Array.Empty<GraphPath>()));
        }

        public async Task<Result<IReadOnlyList<SearchResult>>> RetrieveGraphAwareAsync(string query, int topK, CancellationToken cancellationToken = default)
        {
            // Pass through to underlying rags service
            var result = await _ragsService.RetrieveAsync(new RetrievalRequest(query, topK), cancellationToken).ConfigureAwait(false);
            if (result.IsFailure || result.Value is null)
            {
                return Result<IReadOnlyList<SearchResult>>.Failure(result.Error ?? "Failure");
            }
            return Result<IReadOnlyList<SearchResult>>.Success(result.Value.Take(topK * 2).ToList());
        }

        public Task<Result<IReadOnlyList<GraphNode>>> SelectEntitiesAsync(string query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(Array.Empty<GraphNode>()));
        }

        public Task<Result<IReadOnlyList<GraphCommunity>>> SelectCommunitiesAsync(string query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphCommunity>>.Success(Array.Empty<GraphCommunity>()));
        }
    }

    private sealed class MockRagsService : IRagsService
    {
        public bool Ingested { get; private set; }

        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            Ingested = true;
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
        {
            var results = Enumerable.Range(0, Math.Min(request.TopK, 3))
                .Select(i => new SearchResult(new Chunk(Guid.NewGuid(), Guid.NewGuid(), $"chunk {i}", i), 0.95f - (i * 0.01f)))
                .ToList();
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(results));
        }
    }

    private sealed class FailingRagsService : IRagsService
    {
        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Failure("ingest failed"));
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Failure("retrieve failed"));
        }
    }

    private sealed class FailingRagsRetrieveService : IRagsService
    {
        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Failure("retrieve failed"));
        }
    }

    private sealed class MockGraphProvider : IGraphProvider
    {
        public List<GraphNode> NodesCreated { get; } = new();
        public List<GraphEdge> EdgesCreated { get; } = new();

        public Task<Result<GraphNode?>> GetNodeAsync(string id, CancellationToken cancellationToken = default)
        {
            var node = NodesCreated.FirstOrDefault(n => n.Id == id);
            return Task.FromResult(Result<GraphNode?>.Success(node));
        }

        public Task<Result> CreateNodeAsync(GraphNode node, CancellationToken cancellationToken = default)
        {
            NodesCreated.Add(node);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> UpdateNodeAsync(GraphNode node, CancellationToken cancellationToken = default)
        {
            var index = NodesCreated.FindIndex(n => n.Id == node.Id);
            if (index >= 0)
            {
                NodesCreated[index] = node;
            }
            else
            {
                NodesCreated.Add(node);
            }

            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteNodeAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<GraphEdge?>> GetRelationshipAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<GraphEdge?>.Success(null));
        }

        public Task<Result> CreateRelationshipAsync(GraphEdge edge, CancellationToken cancellationToken = default)
        {
            EdgesCreated.Add(edge);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteRelationshipAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<GraphNode>>> GetNodesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(NodesCreated.AsReadOnly()));
        }

        public Task<Result<IReadOnlyList<GraphEdge>>> GetEdgesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphEdge>>.Success(EdgesCreated.AsReadOnly()));
        }

        public Task<Result<IReadOnlyList<GraphNode>>> GetNeighborsAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            var neighbors = NodesCreated
                .Where(n => EdgesCreated.Any(e =>
                    (e.SourceId == nodeId && e.TargetId == n.Id) ||
                    (e.TargetId == nodeId && e.SourceId == n.Id)))
                .ToList();
            return Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(neighbors));
        }

        public Task<Result<IReadOnlyList<GraphNode>>> SearchNodesAsync(string label, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(NodesCreated.AsReadOnly()));
        }

        public Task<Result<IReadOnlyList<GraphEdge>>> SearchRelationshipsAsync(string type, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphEdge>>.Success(EdgesCreated.AsReadOnly()));
        }

        public Task<Result<IReadOnlyList<GraphPath>>> FindPathsAsync(string startNodeId, string endNodeId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphPath>>.Success(new List<GraphPath>()));
        }

        public Task<Result<IReadOnlyList<GraphNode>>> GetSubgraphAsync(string nodeId, int depth, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(NodesCreated.AsReadOnly()));
        }

        public Task<Result<bool>> GraphExistsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<bool>.Success(NodesCreated.Count > 0));
        }

        public Task<Result> ClearAsync(CancellationToken cancellationToken = default)
        {
            NodesCreated.Clear();
            EdgesCreated.Clear();
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class MockGraphSummaryService : IGraphSummaryService
    {
        public Task<Result<string>> SummarizeEntityAsync(string entityId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<string>.Success($"Summary of {entityId}"));
        }

        public Task<Result<string>> SummarizeCommunityAsync(string communityId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<string>.Success($"Summary of community {communityId}"));
        }

        public Task<Result<string>> SummarizeClusterAsync(string clusterId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<string>.Success($"Summary of cluster {clusterId}"));
        }

        public Task<Result<string>> SummarizeGlobalAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<string>.Success("Global summary"));
        }
    }

    private sealed class MockHierarchicalSummaryService : IHierarchicalSummaryService
    {
        public Task<Result<string>> SummarizeDocumentAsync(string documentId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<string>.Success($"Document summary of {documentId}"));
        }

        public Task<Result<string>> SummarizeEntityAsync(string entityId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<string>.Success($"Hierarchical entity summary of {entityId}"));
        }

        public Task<Result<string>> SummarizeCommunityAsync(string communityId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<string>.Success($"Hierarchical community summary of {communityId}"));
        }

        public Task<Result<string>> SummarizeKnowledgeAreaAsync(string areaId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<string>.Success($"Knowledge area summary of {areaId}"));
        }

        public Task<Result<string>> SummarizeGlobalAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<string>.Success("Hierarchical global summary"));
        }
    }

    private sealed class MockCommunityDetectionService : ICommunityDetectionService
    {
        private readonly IGraphProvider _provider;

        public MockCommunityDetectionService(IGraphProvider provider)
        {
            _provider = provider;
        }

        public Task<Result<IReadOnlyList<GraphCommunity>>> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphCommunity>>.Success(new List<GraphCommunity>()));
        }

        public Task<Result<IReadOnlyList<GraphCommunity>>> DetectClustersAsync(CancellationToken cancellationToken = default)
        {
            return DiscoverAsync(cancellationToken);
        }

        public Task<Result> AssignAsync(string nodeId, string communityId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<GraphCommunity?>> GetCommunityAsync(string communityId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<GraphCommunity?>.Success(null));
        }

        public async Task<Result<IReadOnlyList<GraphCommunity>>> GetCommunitiesForNodeAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            var nodeResult = await _provider.GetNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);
            if (nodeResult.IsSuccess && nodeResult.Value is not null)
            {
                var community = new GraphCommunity
                {
                    Id = $"mock-community-{nodeId}",
                    Name = $"Mock Community for {nodeId}",
                    MemberIds = new List<string> { nodeId }
                };
                return Result<IReadOnlyList<GraphCommunity>>.Success(new[] { community });
            }
            return Result<IReadOnlyList<GraphCommunity>>.Success(Array.Empty<GraphCommunity>());
        }
    }

    private sealed class StoredSummaryGraphSummaryService : IGraphSummaryService
    {
        private readonly IGraphProvider _provider;

        public StoredSummaryGraphSummaryService(IGraphProvider provider)
        {
            _provider = provider;
        }

        public async Task<Result<string>> SummarizeEntityAsync(string entityId, CancellationToken cancellationToken = default)
        {
            var node = await _provider.GetNodeAsync(entityId, cancellationToken).ConfigureAwait(false);
            if (node.IsSuccess &&
                node.Value is not null &&
                node.Value.Properties.TryGetValue("summary", out var summary) &&
                summary is string text)
            {
                return Result<string>.Success(text);
            }

            return Result<string>.Failure("summary missing");
        }

        public Task<Result<string>> SummarizeCommunityAsync(string communityId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<string>.Success($"Stored community summary {communityId}"));
        }

        public Task<Result<string>> SummarizeClusterAsync(string clusterId, CancellationToken cancellationToken = default)
        {
            return SummarizeCommunityAsync(clusterId, cancellationToken);
        }

        public Task<Result<string>> SummarizeGlobalAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<string>.Success("Stored global summary"));
        }
    }

    private sealed class TopLevelCommunityDetectionService : ICommunityDetectionService
    {
        private readonly IReadOnlyList<GraphCommunity> _communities = new[]
        {
            new GraphCommunity
            {
                Id = "lower",
                Name = "Lower Community",
                Description = "Granular implementation details.",
                MemberIds = new[] { "entity-alpha" },
                Metadata = new Dictionary<string, object> { ["level"] = 0 }
            },
            new GraphCommunity
            {
                Id = "top",
                Name = "Top Community",
                Description = "Broad corpus themes.",
                MemberIds = new[] { "entity-alpha", "entity-beta" },
                Metadata = new Dictionary<string, object> { ["level"] = 1 }
            }
        };

        public Task<Result<IReadOnlyList<GraphCommunity>>> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphCommunity>>.Success(_communities));
        }

        public Task<Result<IReadOnlyList<GraphCommunity>>> DetectClustersAsync(CancellationToken cancellationToken = default)
        {
            return DiscoverAsync(cancellationToken);
        }

        public Task<Result> AssignAsync(string nodeId, string communityId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<GraphCommunity?>> GetCommunityAsync(string communityId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<GraphCommunity?>.Success(
                _communities.FirstOrDefault(c => c.Id == communityId)));
        }

        public Task<Result<IReadOnlyList<GraphCommunity>>> GetCommunitiesForNodeAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphCommunity>>.Success(
                _communities.Where(c => c.MemberIds.Contains(nodeId)).ToList()));
        }
    }

    private sealed class MockGraphContextBuilder : IGraphContextBuilder
    {
        public Task<Result<string>> BuildContextAsync(string query, GraphContextSources sources, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<string>.Success($"Mock context for: {query}"));
        }
    }

    private sealed class MockCitationPathService : ICitationPathService
    {
        public Task<Result<IReadOnlyList<string>>> GetDocumentSourcesAsync(string resultId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<string>>.Success(new List<string> { $"Source-{resultId}" }));
        }

        public Task<Result<IReadOnlyList<string>>> GetEntitySourcesAsync(string entityId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<string>>.Success(new List<string> { $"EntitySource-{entityId}" }));
        }

        public Task<Result<IReadOnlyList<string>>> GetRelationshipSourcesAsync(string relationshipId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<string>>.Success(new List<string> { $"RelSource-{relationshipId}" }));
        }

        public Task<Result<IReadOnlyList<GraphPath>>> GetGraphPathsAsync(string fromId, string toId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphPath>>.Success(new List<GraphPath>()));
        }
    }

    private sealed class MockGlobalGraphSearchService : IGlobalGraphSearchService
    {
        public Task<Result<GlobalSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            var result = new GlobalSearchResult(
                $"Global answer for: {query}",
                new List<string> { "Citation-1" },
                Array.Empty<SearchResult>());
            return Task.FromResult(Result<GlobalSearchResult>.Success(result));
        }
    }

    private sealed class RecordingLazyEnrichmentKnowledgeSink : ILazyEnrichmentKnowledgeSink
    {
        public int Calls { get; private set; }

        public List<ExtractedEntity> Entities { get; } = new();

        public List<ExtractedRelationship> Relationships { get; } = new();

        public Task<Result> RecordAsync(
            Guid sourceId,
            string? sourceName,
            IReadOnlyList<ExtractedEntity> entities,
            IReadOnlyList<ExtractedRelationship> relationships,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Entities.AddRange(entities);
            Relationships.AddRange(relationships);
            return Task.FromResult(Result.Success());
        }
    }
}
