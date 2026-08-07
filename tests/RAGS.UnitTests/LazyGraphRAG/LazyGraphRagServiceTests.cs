using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Interfaces;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.LazyGraphRAG;
using Aletheia.RAGS.Application.Pipelines;

namespace RAGS.UnitTests.LazyGraphRAG;

public sealed class LazyGraphRagServiceTests
{
    [Fact]
    public async Task IngestAsync_stores_chunks_and_creates_graph_nodes()
    {
        var ragsService = new MockRagsService();
        var service = CreateService(ragsService);
        var sourceId = Guid.NewGuid();

        var result = await service.IngestAsync(new IngestionRequest(sourceId, new string('a', 3000)));

        Assert.True(result.IsSuccess);
        Assert.True(ragsService.Ingested);
    }

    [Fact]
    public async Task IngestAsync_fails_when_rags_service_fails()
    {
        var ragsService = new FailingRagsService();
        var service = CreateService(ragsService);

        var result = await service.IngestAsync(new IngestionRequest(Guid.NewGuid(), "test content"));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task IngestAsync_indexes_statistics_without_lazy_llm_discovery()
    {
        var ragsService = new MockRagsService();
        var lazyDiscovery = new CountingLazyEntityDiscoveryService();
        var lazyRelationships = new CountingLazyRelationshipDiscoveryService();
        var service = CreateService(
            ragsService,
            lazyDiscovery: lazyDiscovery,
            lazyRelationshipDiscovery: lazyRelationships);

        var result = await service.IngestAsync(new IngestionRequest(
            Guid.NewGuid(),
            "alpha alpha beta launch governance telemetry"));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, lazyDiscovery.DiscoverCalls);
        Assert.Equal(0, lazyRelationships.DiscoverCalls);
    }

    [Fact]
    public async Task RetrieveAsync_returns_results_with_neighbors_and_topK()
    {
        var ragsService = new MockRagsService();
        var service = CreateService(ragsService);

        var result = await service.RetrieveAsync("query", topK: 2, maxExpanded: 10);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Count >= 2);
        Assert.All(result.Value, item =>
        {
            Assert.True(item.Rank > 0);
            Assert.NotEmpty(item.Citations);
            Assert.NotEmpty(item.RankingSignals);
            Assert.Contains("final", item.RankingSignals.Keys);
            Assert.Contains("lazy", item.RetrievalStrategy);
        });
    }

    [Fact]
    public async Task RetrieveAsync_returns_failure_when_rags_retrieval_fails()
    {
        var ragsService = new FailingRagsRetrieveService();
        var service = CreateService(ragsService);

        var result = await service.RetrieveAsync("query");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task RetrieveAsync_uses_query_time_relationship_guidance_under_budget()
    {
        var ragsService = new MockRagsService();
        var lazyRelationships = new CountingLazyRelationshipDiscoveryService();
        var service = CreateService(
            ragsService,
            budget: new GraphTraversalBudget(maxLLMCalls: 30, maxDepth: 2, maxNodes: 100, maxRelationships: 100),
            lazyRelationshipDiscovery: lazyRelationships);

        await service.IngestAsync(new IngestionRequest(
            Guid.NewGuid(),
            "alpha beta launch roadmap alpha beta governance"));

        var result = await service.RetrieveAsync("alpha beta launch", topK: 2);

        Assert.True(result.IsSuccess);
        Assert.True(lazyRelationships.DiscoverCalls > 0);
        Assert.All(result.Value!, item => Assert.Contains("lazy", item.RetrievalStrategy));
    }

    [Fact]
    public async Task RetrieveAsync_does_not_fail_when_optional_summary_work_reaches_default_budget()
    {
        var ragsService = new MockRagsService();
        var lazyRelationships = new CountingLazyRelationshipDiscoveryService();
        var service = CreateService(
            ragsService,
            lazyRelationshipDiscovery: lazyRelationships);

        await service.IngestAsync(new IngestionRequest(
            Guid.NewGuid(),
            "Project Helios Alpha works with Beta for retrieval validation taxonomy ontology telemetry governance."));

        var result = await service.RetrieveAsync("Project Helios Alpha Beta", topK: 3, maxExpanded: 2);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value!);
    }

    [Fact]
    public async Task RetrieveAsync_uses_a_per_request_budget_and_does_not_mutate_the_template()
    {
        var ragsService = new MockRagsService();
        var lazyRelationships = new CountingLazyRelationshipDiscoveryService();
        var template = new GraphTraversalBudget(maxLLMCalls: 30, maxDepth: 2, maxNodes: 100, maxRelationships: 100);
        var service = CreateService(
            ragsService,
            budget: template,
            lazyRelationshipDiscovery: lazyRelationships);

        await service.IngestAsync(new IngestionRequest(
            Guid.NewGuid(),
            "alpha beta launch roadmap alpha beta governance"));

        var result = await service.RetrieveAsync("alpha beta launch", topK: 2);

        Assert.True(result.IsSuccess);
        Assert.True(lazyRelationships.DiscoverCalls > 0);
        // The template budget itself must never be mutated: each request works on its own copy.
        Assert.Equal(0, template.LlmCalls);
        Assert.Equal(0, template.NodesVisited);
        Assert.Equal(0, template.RelationshipsTraversed);
        Assert.Equal(0, template.TokensConsumed);
    }

    [Fact]
    public async Task Concurrent_retrievals_do_not_corrupt_each_other_budgets()
    {
        var ragsService = new MockRagsService();
        var lazyRelationships = new CountingLazyRelationshipDiscoveryService();
        var template = new GraphTraversalBudget();
        var service = CreateService(ragsService, budget: template, lazyRelationshipDiscovery: lazyRelationships);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => service.RetrieveAsync("alpha beta launch", topK: 2))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.True(lazyRelationships.DiscoverCalls > 0);
        Assert.Equal(0, template.LlmCalls);
        Assert.Equal(0, template.TokensConsumed);
    }

    [Fact]
    public async Task RetrieveAsync_populates_retrieval_trace()
    {
        var ragsService = new MockRagsService();
        var service = CreateService(ragsService);

        var result = await service.RetrieveAsync("alpha beta launch", topK: 2);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value!);
        var first = result.Value!.First();
        Assert.NotNull(first.Trace);
        Assert.False(string.IsNullOrEmpty(first.Trace!.Strategy));
        Assert.NotEmpty(first.Trace.Steps);
        Assert.True(first.Trace.ElapsedMs >= 0);
        Assert.Contains("traversal", first.Trace.Steps);
    }

    private static LazyGraphRagService CreateService(
        IRagsService ragsService,
        IGraphTraversalBudget? budget = null,
        ILazyEntityDiscoveryService? lazyDiscovery = null,
        ILazyRelationshipDiscoveryService? lazyRelationshipDiscovery = null)
    {
        var graphProvider = new MockGraphProvider();
        return new LazyGraphRagService(
            ragsService,
            new ChunkingPipeline(),
            new CorpusDiscoveryIndex(),
            new MockGraphReasoningService(ragsService, graphProvider),
            new SubgraphPruningService(),
            new MockGraphSummaryService(),
            new MockHierarchicalSummaryService(),
            new MockCommunityDetectionService(),
            new MockGraphContextBuilder(),
            new MockCitationPathService(),
            new MockGlobalGraphSearchService(),
            graphProvider,
            lazyDiscovery,
            lazyRelationshipDiscovery,
            budget ?? new GraphTraversalBudget());
    }

    private sealed class CountingLazyEntityDiscoveryService : ILazyEntityDiscoveryService
    {
        public int DiscoverCalls { get; private set; }

        public Task<Result<IReadOnlyList<ExtractedEntity>>> DiscoverAtQueryTimeAsync(string query, IGraphTraversalBudget? budget = null, CancellationToken cancellationToken = default)
        {
            DiscoverCalls++;
            return Task.FromResult(Result<IReadOnlyList<ExtractedEntity>>.Success(new[]
            {
                new ExtractedEntity { Id = "lazy:alpha", Name = "alpha", Type = "keyword", Confidence = 0.8 }
            }));
        }

        public Task<Result<IReadOnlyList<ExtractedEntity>>> CreateIncrementalAsync(string text, CancellationToken cancellationToken = default)
        {
            DiscoverCalls++;
            return Task.FromResult(Result<IReadOnlyList<ExtractedEntity>>.Success(Array.Empty<ExtractedEntity>()));
        }

        public Task<Result> PersistAsync(IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class CountingLazyRelationshipDiscoveryService : ILazyRelationshipDiscoveryService
    {
        public int DiscoverCalls { get; private set; }

        public Task<Result<IReadOnlyList<ExtractedRelationship>>> DiscoverAtQueryTimeAsync(
            string query,
            IReadOnlyList<ExtractedEntity> entities,
            IGraphTraversalBudget? budget = null,
            CancellationToken cancellationToken = default)
        {
            DiscoverCalls++;
            if (entities.Count < 2)
            {
                return Task.FromResult(Result<IReadOnlyList<ExtractedRelationship>>.Success(Array.Empty<ExtractedRelationship>()));
            }

            IReadOnlyList<ExtractedRelationship> relationships = new[]
            {
                new ExtractedRelationship
                {
                    SourceId = entities[0].Id,
                    TargetId = entities[1].Id,
                    Type = "guides",
                    Description = "Guided edge selected at query time.",
                    Confidence = 0.9
                }
            };

            return Task.FromResult(Result<IReadOnlyList<ExtractedRelationship>>.Success(relationships));
        }

        public Task<Result> PersistAsync(IReadOnlyList<ExtractedRelationship> relationships, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
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

        public Task<Result<IReadOnlyList<GraphCommunity>>> GetCommunitiesForNodeAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphCommunity>>.Success(Array.Empty<GraphCommunity>()));
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

    private sealed class MockGraphProvider : IGraphProvider
    {
        public Task<Result<GraphNode?>> GetNodeAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<GraphNode?>.Success(null));
        }

        public Task<Result> CreateNodeAsync(GraphNode node, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result> UpdateNodeAsync(GraphNode node, CancellationToken cancellationToken = default)
        {
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
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteRelationshipAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<GraphNode>>> GetNodesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(Array.Empty<GraphNode>()));
        }

        public Task<Result<IReadOnlyList<GraphEdge>>> GetEdgesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphEdge>>.Success(Array.Empty<GraphEdge>()));
        }

        public Task<Result<IReadOnlyList<GraphNode>>> GetNeighborsAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(Array.Empty<GraphNode>()));
        }

        public Task<Result<IReadOnlyList<GraphNode>>> SearchNodesAsync(string label, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(Array.Empty<GraphNode>()));
        }

        public Task<Result<IReadOnlyList<GraphEdge>>> SearchRelationshipsAsync(string type, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphEdge>>.Success(Array.Empty<GraphEdge>()));
        }

        public Task<Result<IReadOnlyList<GraphPath>>> FindPathsAsync(string startNodeId, string endNodeId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphPath>>.Success(Array.Empty<GraphPath>()));
        }

        public Task<Result<IReadOnlyList<GraphNode>>> GetSubgraphAsync(string nodeId, int depth, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<GraphNode>>.Success(Array.Empty<GraphNode>()));
        }

        public Task<Result<bool>> GraphExistsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<bool>.Success(false));
        }

        public Task<Result> ClearAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
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
}
