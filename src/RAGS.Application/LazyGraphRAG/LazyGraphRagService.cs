using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.GraphIntelligence;
using Aletheia.RAGS.Application.Pipelines;
using Aletheia.RAGS.Application.Ranking;
using System.Text.RegularExpressions;

namespace Aletheia.RAGS.Application.LazyGraphRAG;

/// <summary>
/// LazyGraphRAG service aligned with Microsoft LazyGraphRAG model.
/// Near-zero indexing cost. All intelligence work occurs during retrieval.
/// </summary>
public sealed class LazyGraphRagService : ILazyGraphRagService
{
    private const string RetrievalFailedMessage = "LazyGraphRAG retrieval failed.";

    private readonly IRagsService _ragsService;
    private readonly ChunkingPipeline _chunkingPipeline;
    private readonly ICorpusDiscoveryIndex _corpusIndex;
    private readonly ILazyEntityDiscoveryService? _lazyDiscovery;
    private readonly ILazyRelationshipDiscoveryService? _lazyRelationshipDiscovery;
    private readonly IGraphReasoningService _graphReasoning;
    private readonly IGraphTraversalBudget budgetTemplate;
    private readonly ISubgraphPruningService _pruning;
    private readonly IGraphSummaryService _graphSummary;
    private readonly IHierarchicalSummaryService _hierarchicalSummary;
    private readonly ICommunityDetectionService _communityDetection;
    private readonly IGraphContextBuilder _contextBuilder;
    private readonly ICitationPathService _citationPath;
    private readonly IGlobalGraphSearchService _globalSearch;
    private readonly IGraphProvider _graphProvider;

    private readonly HashSet<Guid> _indexedSources = new();
    private readonly object _indexedSourcesLock = new();

    public LazyGraphRagService(
        IRagsService ragsService,
        ChunkingPipeline chunkingPipeline,
        ICorpusDiscoveryIndex corpusIndex,
        IGraphReasoningService graphReasoning,
        ISubgraphPruningService pruning,
        IGraphSummaryService graphSummary,
        IHierarchicalSummaryService hierarchicalSummary,
        ICommunityDetectionService communityDetection,
        IGraphContextBuilder contextBuilder,
        ICitationPathService citationPath,
        IGlobalGraphSearchService globalSearch,
        IGraphProvider graphProvider,
        ILazyEntityDiscoveryService? lazyDiscovery = null,
        ILazyRelationshipDiscoveryService? lazyRelationshipDiscovery = null,
        IGraphTraversalBudget? budget = null)
    {
        _ragsService = ragsService ?? throw new ArgumentNullException(nameof(ragsService));
        _chunkingPipeline = chunkingPipeline ?? throw new ArgumentNullException(nameof(chunkingPipeline));
        _corpusIndex = corpusIndex ?? throw new ArgumentNullException(nameof(corpusIndex));
        _graphReasoning = graphReasoning ?? throw new ArgumentNullException(nameof(graphReasoning));
        budgetTemplate = budget ?? new GraphTraversalBudget();
        _pruning = pruning ?? throw new ArgumentNullException(nameof(pruning));
        _graphSummary = graphSummary ?? throw new ArgumentNullException(nameof(graphSummary));
        _hierarchicalSummary = hierarchicalSummary ?? throw new ArgumentNullException(nameof(hierarchicalSummary));
        _communityDetection = communityDetection ?? throw new ArgumentNullException(nameof(communityDetection));
        _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
        _citationPath = citationPath ?? throw new ArgumentNullException(nameof(citationPath));
        _globalSearch = globalSearch ?? throw new ArgumentNullException(nameof(globalSearch));
        _graphProvider = graphProvider ?? throw new ArgumentNullException(nameof(graphProvider));
        _lazyDiscovery = lazyDiscovery;
        _lazyRelationshipDiscovery = lazyRelationshipDiscovery;
    }

    // ============ LIGHTWEIGHT INDEXING ============

    public async Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
    {
        var ingestResult = await _ragsService.IngestAsync(request, cancellationToken).ConfigureAwait(false);
        if (ingestResult.IsFailure)
        {
            return ingestResult;
        }

        lock (_indexedSourcesLock)
        {
            if (_indexedSources.Contains(request.SourceId))
            {
                return Result.Success();
            }

            _indexedSources.Add(request.SourceId);
        }

        // Phase 3: no entity, relationship, or graph construction at index time.
        // LazyGraphRAG stores only corpus text statistics used for query-time candidates.
        await _corpusIndex.IndexAsync(request.Content, request.SourceId, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    // ============ RETRIEVAL WITH QUERY-TIME GRAPH CONSTRUCTION ============

    public async Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(
        string query,
        int topK = 5,
        int maxExpanded = 10,
        CancellationToken cancellationToken = default,
        IReadOnlyList<Guid>? sourceIds = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query is required.", nameof(query));
        }

        // Each request gets its own budget so concurrent retrievals cannot corrupt each other.
        var budget = budgetTemplate.CreatePerRequest();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(budget.MaxExecutionTime);
        var ct = timeoutCts.Token;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var steps = new List<string>();

        try
        {
            // Step 0: Corpus search for seed documents
            var seedSourceIds = _corpusIndex.SearchCorpus(query, topK: 10);

            // Sprint 64: theme scope — restrict corpus candidates to the selected sources so
            // entity discovery, traversal, and expansion stay within the scope.
            if (sourceIds is not null && sourceIds.Count > 0)
            {
                var allowed = sourceIds.ToHashSet();
                seedSourceIds = seedSourceIds.Where(allowed.Contains).ToList();
            }
            steps.Add("corpus-search");

            // Step 1: Query-time candidate discovery from corpus statistics only.
            var queryEntities = DiscoverEntitiesAtQueryTime(query, seedSourceIds, budget);
            steps.Add("entity-discovery");
            if (budget.IsExceeded())
                return Result<IReadOnlyList<SearchResult>>.Failure("Traversal budget exceeded during entity discovery.");

            // Step 2: Query-time edge guidance within the configured LLM budget.
            var queryRelationships = await DiscoverRelationshipsAtQueryTimeAsync(query, queryEntities, budget, ct).ConfigureAwait(false);
            steps.Add("relationship-discovery");
            if (budget.IsExceeded())
                return Result<IReadOnlyList<SearchResult>>.Failure("Traversal budget exceeded during relationship discovery.");

            // Step 3: Build temporary query graph from statistical candidates.
            var tempGraph = BuildTemporaryGraph(queryEntities, queryRelationships, seedSourceIds);
            steps.Add("graph-build");

            // Step 4: Budgeted best-first traversal with optional LLM edge guidance.
            var traversal = await TraverseBestFirstAsync(query, tempGraph, queryRelationships, budget, ct).ConfigureAwait(false);
            steps.Add("traversal");
            if (budget.IsExceeded())
                return Result<IReadOnlyList<SearchResult>>.Failure("Traversal budget exceeded during graph traversal.");

            // Step 5: Subgraph Pruning
            var prunedNodes = await _pruning.PruneNodesAsync(traversal.Nodes, query, relevanceThreshold: 0.25f, ct).ConfigureAwait(false);
            var prunedNodeList = prunedNodes.IsSuccess && prunedNodes.Value is not null
                ? prunedNodes.Value.ToList()
                : traversal.Nodes.ToList();

            var prunedEdges = await _pruning.PruneRelationshipsAsync(traversal.Edges, prunedNodeList, ct).ConfigureAwait(false);
            var prunedEdgeList = prunedEdges.IsSuccess && prunedEdges.Value is not null
                ? prunedEdges.Value.ToList()
                : traversal.Edges.ToList();
            steps.Add("pruning");

            // Step 6: Community Resolution & Summary Retrieval
            await ResolveCommunitiesAndSummariesAsync(prunedNodeList, budget, ct).ConfigureAwait(false);
            steps.Add("community-resolution");
            if (budget.IsExceeded())
                return Result<IReadOnlyList<SearchResult>>.Failure("Traversal budget exceeded during community resolution.");

            // Step 7: Context Builder
            var contextResult = await _contextBuilder.BuildContextAsync(
                query,
                GraphContextSources.Entities | GraphContextSources.Communities | GraphContextSources.Summaries | GraphContextSources.Relationships,
                ct).ConfigureAwait(false);
            var contextScore = HasUsableContext(contextResult) ? 0.55f : 0f;
            steps.Add("context-build");

            // Step 8: Semantic Retrieval & Expansion
            var baseResults = await _ragsService.RetrieveAsync(new RetrievalRequest(query, topK, sourceIds: sourceIds), ct).ConfigureAwait(false);
            if (baseResults.IsFailure || baseResults.Value is null)
            {
                return Result<IReadOnlyList<SearchResult>>.Failure(baseResults.Error ?? RetrievalFailedMessage);
            }

            var candidates = baseResults.Value
                .Select(r => new RetrievalCandidate(
                    r,
                    "lazy-semantic",
                    graphScore: prunedNodeList.Any() ? 0.25f : 0f,
                    contextScore: contextScore))
                .ToList();
            steps.Add("semantic-retrieval");

            // Expand using seed source IDs from corpus search
            foreach (var sourceId in seedSourceIds.Take(maxExpanded))
            {
                var terms = _corpusIndex.GetTerms(sourceId);
                if (!terms.Any()) continue;

                var expanded = await _ragsService.RetrieveAsync(
                    new RetrievalRequest(string.Join(" ", terms.Take(5)), Math.Min(2, topK), sourceIds: sourceIds), ct).ConfigureAwait(false);

                if (expanded.IsSuccess && expanded.Value is not null)
                {
                    foreach (var item in expanded.Value)
                    {
                        candidates.Add(new RetrievalCandidate(
                            new SearchResult(item.Chunk, item.Score * 0.6f),
                            "lazy-corpus-expansion",
                            graphScore: 0.35f + Math.Min(0.15f, prunedEdgeList.Count * 0.01f),
                            contextScore: contextScore));
                    }
                }
            }

            // Expand using traversed entity labels
            foreach (var node in prunedNodeList.Take(maxExpanded))
            {
                var entityResults = await _ragsService.RetrieveAsync(
                    new RetrievalRequest(node.Label, Math.Min(2, topK), sourceIds: sourceIds), ct).ConfigureAwait(false);

                if (entityResults.IsSuccess && entityResults.Value is not null)
                {
                    foreach (var item in entityResults.Value)
                    {
                        candidates.Add(new RetrievalCandidate(
                            new SearchResult(item.Chunk, item.Score * 0.5f),
                            "lazy-entity-expansion",
                            graphScore: node.Type.Equals("lazy-entity", StringComparison.OrdinalIgnoreCase) ? 0.55f : 0.7f,
                            contextScore: contextScore));
                    }
                }
            }
            steps.Add("expansion");

            var finalResults = await GraphRagResultRanker.RankAndCiteAsync(
                candidates,
                _citationPath,
                topK * 2,
                ct).ConfigureAwait(false);
            steps.Add("ranking");

            // Step 10: Persistent Enrichment (noise entities are never persisted)
            await PersistDiscoveryAsync(queryEntities, queryRelationships, ct).ConfigureAwait(false);
            steps.Add("persist");

            var trace = new RetrievalTrace
            {
                Strategy = finalResults.FirstOrDefault()?.RetrievalStrategy ?? "lazy-graph",
                LlmCalls = budget.LlmCalls,
                TokensConsumed = budget.TokensConsumed,
                NodesVisited = budget.NodesVisited,
                RelationshipsTraversed = budget.RelationshipsTraversed,
                PruningRatio = traversal.Nodes.Count > 0 ? prunedNodeList.Count / (double)traversal.Nodes.Count : null,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Steps = steps,
            };

            return Result<IReadOnlyList<SearchResult>>.Success(WithTrace(finalResults, trace));
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<SearchResult>>.Failure($"{RetrievalFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<GlobalSearchResult>> GlobalSearchAsync(
        string query,
        CancellationToken cancellationToken = default,
        IReadOnlyList<Guid>? sourceIds = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query is required.", nameof(query));
        }

        return await _globalSearch.SearchAsync(query, cancellationToken, sourceIds).ConfigureAwait(false);
    }

    // ============ QUERY-TIME DISCOVERY ============

    private List<ExtractedEntity> DiscoverEntitiesAtQueryTime(string query, IReadOnlyList<Guid> seedSourceIds, IGraphTraversalBudget budget)
    {
        var queryTerms = ExtractTerms(query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scored = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in queryTerms)
        {
            scored[term] = scored.GetValueOrDefault(term) + 1.0;
        }

        foreach (var sourceId in seedSourceIds.Take(10))
        {
            foreach (var term in _corpusIndex.GetTerms(sourceId).Take(100))
            {
                var bm25 = _corpusIndex.GetBm25Score(term, sourceId);
                var tfidf = _corpusIndex.GetTfIdf(term, sourceId);
                var queryBoost = queryTerms.Contains(term) ? 1.0 : 0.0;
                var score = bm25 + tfidf + queryBoost;
                if (score <= 0)
                {
                    continue;
                }

                scored[term] = scored.GetValueOrDefault(term) + score;
            }
        }

        return scored
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Min(20, budget.MaxNodes))
            .Select(kv => new ExtractedEntity
            {
                Id = $"lazy:{kv.Key}",
                Name = kv.Key,
                Type = "statistical-candidate",
                Confidence = Math.Min(0.95, 0.35 + kv.Value / 10.0),
                Properties = new Dictionary<string, object>
                {
                    ["discoveryMode"] = "query-time-statistical",
                    ["score"] = kv.Value
                }
            })
            .ToList();
    }

    private async Task<List<ExtractedRelationship>> DiscoverRelationshipsAtQueryTimeAsync(
        string query,
        List<ExtractedEntity> entities,
        IGraphTraversalBudget budget,
        CancellationToken ct)
    {
        if (_lazyRelationshipDiscovery is null || entities.Count < 2 || !budget.RecordLLMCall())
        {
            return new List<ExtractedRelationship>();
        }

        var result = await _lazyRelationshipDiscovery.DiscoverAtQueryTimeAsync(query, entities, budget, ct).ConfigureAwait(false);
        if (result.IsSuccess && result.Value is not null)
        {
            return result.Value.ToList();
        }

        return new List<ExtractedRelationship>();
    }

    // ============ TEMPORARY GRAPH & TRAVERSAL ============

    private TemporaryGraph BuildTemporaryGraph(
        List<ExtractedEntity> entities,
        List<ExtractedRelationship> relationships,
        IReadOnlyList<Guid> seedSourceIds)
    {
        var nodes = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<GraphEdge>();

        foreach (var entity in entities)
        {
            if (!nodes.ContainsKey(entity.Id))
            {
                nodes[entity.Id] = new GraphNode(
                    entity.Id,
                    entity.Name,
                    entity.Type,
                    new Dictionary<string, object>
                    {
                        ["confidence"] = entity.Confidence,
                        ["description"] = entity.Description ?? string.Empty,
                    });
            }
        }

        foreach (var relationship in relationships)
        {
            if (nodes.ContainsKey(relationship.SourceId) && nodes.ContainsKey(relationship.TargetId))
            {
                edges.Add(new GraphEdge(
                    string.IsNullOrWhiteSpace(relationship.Id)
                        ? $"{relationship.SourceId}-{relationship.TargetId}-{relationship.Type}"
                        : relationship.Id,
                    relationship.SourceId,
                    relationship.TargetId,
                    string.IsNullOrWhiteSpace(relationship.Type) ? "guided_related_to" : NormalizeRelationshipType(relationship.Type),
                    new Dictionary<string, object>
                    {
                        ["confidence"] = relationship.Confidence,
                        ["description"] = relationship.Description ?? string.Empty,
                        ["discoveryMode"] = "query-time-guided"
                    }));
            }
        }

        // Add statistical relatedness edges only at query time. No index-time graph is retained.
        var entityList = entities.Take(Math.Min(entities.Count, 12)).ToList();
        for (var i = 0; i < entityList.Count; i++)
        {
            for (var j = i + 1; j < entityList.Count; j++)
            {
                var source = entityList[i];
                var target = entityList[j];
                var relatedness = ComputeStatisticalRelatedness(source.Name, target.Name, seedSourceIds);
                if (relatedness <= 0)
                {
                    continue;
                }

                edges.Add(new GraphEdge(
                    $"stat:{source.Id}:{target.Id}",
                    source.Id,
                    target.Id,
                    "statistically_related",
                    new Dictionary<string, object>
                    {
                        ["score"] = relatedness,
                        ["discoveryMode"] = "query-time-statistical"
                    }));
            }
        }

        return new TemporaryGraph(nodes.Values.ToList(), edges);
    }

    private async Task<TemporaryGraph> TraverseBestFirstAsync(
        string query,
        TemporaryGraph seedGraph,
        IReadOnlyList<ExtractedRelationship> guidedRelationships,
        IGraphTraversalBudget budget,
        CancellationToken ct)
    {
        var reasoningResult = await _graphReasoning.SelectEntitiesAsync(query, ct).ConfigureAwait(false);
        var selectedNodes = reasoningResult.IsSuccess && reasoningResult.Value is not null
            ? reasoningResult.Value.ToList()
            : new List<GraphNode>();

        if (!budget.RecordNodeVisit())
            return seedGraph;

        var merged = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        var retainedEdges = new List<GraphEdge>(seedGraph.Edges);
        var edgeLookup = BuildEdgeLookup(seedGraph.Edges);

        foreach (var node in seedGraph.Nodes)
        {
            merged[node.Id] = node;
        }
        foreach (var node in selectedNodes)
        {
            merged[node.Id] = node;
        }

        var frontier = new PriorityQueue<(GraphNode Node, int Depth), double>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in merged.Values
            .OrderByDescending(n => ComputeNodeRelevance(n, query))
            .Take(Math.Min(8, budget.MaxNodes)))
        {
            frontier.Enqueue((node, 0), -ComputeNodeRelevance(node, query));
            visited.Add(node.Id);
        }

        while (frontier.Count > 0 && !budget.IsExceeded())
        {
            var (current, depth) = frontier.Dequeue();
            if (depth >= budget.MaxDepth) continue;
            if (!budget.RecordNodeVisit())
                break;

            foreach (var edge in GetCandidateEdges(current.Id, edgeLookup).OrderByDescending(e => ComputeEdgeRelevance(e, query)).Take(6))
            {
                if (!budget.RecordRelationshipTraversed())
                {
                    break;
                }

                var targetId = edge.SourceId.Equals(current.Id, StringComparison.OrdinalIgnoreCase)
                    ? edge.TargetId
                    : edge.SourceId;

                if (!merged.TryGetValue(targetId, out var targetNode) || visited.Contains(targetNode.Id))
                {
                    continue;
                }

                retainedEdges.Add(edge);
                visited.Add(targetNode.Id);
                frontier.Enqueue((targetNode, depth + 1), -ComputeTraversalPriority(targetNode, edge, query, depth + 1));
            }

            if (ShouldUseGuidedNeighborSelection(guidedRelationships) && !budget.RecordLLMCall())
            {
                continue;
            }

            var neighbors = await _graphProvider.GetNeighborsAsync(current.Id, ct).ConfigureAwait(false);
            if (neighbors.IsSuccess && neighbors.Value is not null)
            {
                foreach (var neighbor in neighbors.Value
                    .OrderByDescending(n => ComputeNodeRelevance(n, query))
                    .Take(6))
                {
                    if (!visited.Contains(neighbor.Id) && merged.Count < budget.MaxNodes)
                    {
                        visited.Add(neighbor.Id);
                        merged[neighbor.Id] = neighbor;
                        var edge = new GraphEdge(
                            $"provider:{current.Id}:{neighbor.Id}",
                            current.Id,
                            neighbor.Id,
                            "provider_neighbor",
                            new Dictionary<string, object>
                            {
                                ["score"] = ComputeNodeRelevance(neighbor, query),
                                ["discoveryMode"] = "query-time-best-first"
                            });
                        retainedEdges.Add(edge);
                        frontier.Enqueue((neighbor, depth + 1), -ComputeTraversalPriority(neighbor, edge, query, depth + 1));
                    }
                }
            }
        }

        return new TemporaryGraph(merged.Values.ToList(), retainedEdges);
    }

    // ============ COMMUNITY & SUMMARY RESOLUTION ============

    private async Task ResolveCommunitiesAndSummariesAsync(List<GraphNode> nodes, IGraphTraversalBudget budget, CancellationToken ct)
    {
        var communityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes.Take(5))
        {
            if (!budget.RecordLLMCall()) break;

            var communitiesResult = await _communityDetection.GetCommunitiesForNodeAsync(node.Id, ct).ConfigureAwait(false);
            if (communitiesResult.IsSuccess && communitiesResult.Value is not null)
            {
                foreach (var community in communitiesResult.Value)
                {
                    communityIds.Add(community.Id);
                }
            }
        }

        foreach (var node in nodes.Take(3))
        {
            if (!budget.RecordLLMCall()) break;
            await _graphSummary.SummarizeEntityAsync(node.Id, ct).ConfigureAwait(false);
            await _hierarchicalSummary.SummarizeEntityAsync(node.Id, ct).ConfigureAwait(false);
        }

        foreach (var communityId in communityIds.Take(3))
        {
            if (!budget.RecordLLMCall()) break;
            await _graphSummary.SummarizeCommunityAsync(communityId, ct).ConfigureAwait(false);
            await _hierarchicalSummary.SummarizeCommunityAsync(communityId, ct).ConfigureAwait(false);
        }
    }

    // ============ PERSISTENT ENRICHMENT ============

    private async Task PersistDiscoveryAsync(
        List<ExtractedEntity> entities,
        List<ExtractedRelationship> relationships,
        CancellationToken ct)
    {
        // Noise entities (keyword / statistical-candidate) are retrieval-only signals and
        // must never be persisted as graph nodes. Relationships between noise entities are
        // dropped too, so no dangling edges are written.
        var persistableEntities = entities.Where(e => !NoiseEntityFilter.IsNoise(e)).ToList();
        var persistableIds = persistableEntities.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (_lazyDiscovery is not null && persistableEntities.Any())
        {
            await _lazyDiscovery.PersistAsync(persistableEntities, ct).ConfigureAwait(false);
        }

        if (_lazyRelationshipDiscovery is not null && relationships.Any())
        {
            var persistableRelationships = relationships
                .Where(r => persistableIds.Contains(r.SourceId) && persistableIds.Contains(r.TargetId))
                .ToList();

            if (persistableRelationships.Any())
            {
                await _lazyRelationshipDiscovery.PersistAsync(persistableRelationships, ct).ConfigureAwait(false);
            }
        }
    }

    // ============ UTILITIES ============

    private double ComputeStatisticalRelatedness(string sourceTerm, string targetTerm, IReadOnlyList<Guid> seedSourceIds)
    {
        var score = 0.0;
        foreach (var sourceId in seedSourceIds)
        {
            var sourceScore = _corpusIndex.GetBm25Score(sourceTerm, sourceId) + _corpusIndex.GetTfIdf(sourceTerm, sourceId);
            var targetScore = _corpusIndex.GetBm25Score(targetTerm, sourceId) + _corpusIndex.GetTfIdf(targetTerm, sourceId);
            if (sourceScore > 0 && targetScore > 0)
            {
                score += Math.Sqrt(sourceScore * targetScore);
            }
        }

        return score;
    }

    private static Dictionary<string, List<GraphEdge>> BuildEdgeLookup(IReadOnlyList<GraphEdge> edges)
    {
        var lookup = new Dictionary<string, List<GraphEdge>>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges)
        {
            if (!lookup.TryGetValue(edge.SourceId, out var sourceEdges))
            {
                sourceEdges = new List<GraphEdge>();
                lookup[edge.SourceId] = sourceEdges;
            }
            sourceEdges.Add(edge);

            if (!lookup.TryGetValue(edge.TargetId, out var targetEdges))
            {
                targetEdges = new List<GraphEdge>();
                lookup[edge.TargetId] = targetEdges;
            }
            targetEdges.Add(edge);
        }

        return lookup;
    }

    private static IReadOnlyList<GraphEdge> GetCandidateEdges(string nodeId, IReadOnlyDictionary<string, List<GraphEdge>> edgeLookup)
    {
        return edgeLookup.TryGetValue(nodeId, out var edges) ? edges : Array.Empty<GraphEdge>();
    }

    private static double ComputeTraversalPriority(GraphNode node, GraphEdge edge, string query, int depth)
    {
        var relevance = ComputeNodeRelevance(node, query);
        var edgeRelevance = ComputeEdgeRelevance(edge, query);
        var depthPenalty = depth * 0.08;
        return Math.Max(0, relevance + edgeRelevance - depthPenalty);
    }

    private static double ComputeNodeRelevance(GraphNode node, string query)
    {
        var queryTerms = ExtractTerms(query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var labelTerms = ExtractTerms(node.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var score = queryTerms.Count == 0 ? 0 : labelTerms.Count(queryTerms.Contains) / (double)queryTerms.Count;

        foreach (var property in node.Properties.Values)
        {
            if (property is not string value)
            {
                continue;
            }

            var propertyTerms = ExtractTerms(value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            score += propertyTerms.Count(queryTerms.Contains) * 0.05;
        }

        if (node.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            query.Contains(node.Label, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.5;
        }

        return Math.Min(1.0, score);
    }

    private static double ComputeEdgeRelevance(GraphEdge edge, string query)
    {
        var queryTerms = ExtractTerms(query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var edgeTerms = ExtractTerms(edge.RelationshipType)
            .Concat(edge.Properties.Values.OfType<string>().SelectMany(ExtractTerms))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var score = queryTerms.Count == 0 ? 0 : edgeTerms.Count(queryTerms.Contains) / (double)queryTerms.Count;
        if (edge.Properties.TryGetValue("confidence", out var confidence) &&
            double.TryParse(confidence?.ToString(), out var parsedConfidence))
        {
            score += parsedConfidence * 0.25;
        }

        if (edge.Properties.TryGetValue("score", out var statScore) &&
            double.TryParse(statScore?.ToString(), out var parsedScore))
        {
            score += Math.Min(0.5, parsedScore);
        }

        return Math.Min(1.0, score);
    }

    private static bool ShouldUseGuidedNeighborSelection(IReadOnlyList<ExtractedRelationship> guidedRelationships)
    {
        return guidedRelationships.Any(r => r.Confidence >= 0.7);
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

    private static IEnumerable<string> ExtractTerms(string text)
    {
        var words = text.Split(new[] { ' ', '.', ',', ';', ':', '!', '?', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
            "and", "or", "but", "of", "in", "on", "at", "to", "for", "with", "by",
            "this", "that", "these", "those", "it", "its", "from", "as", "has", "have"
        };

        return words
            .Select(w => w.Trim().ToLowerInvariant())
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Distinct();
    }

    private static bool HasUsableContext(Result<string> contextResult)
    {
        return contextResult.IsSuccess
            && !string.IsNullOrWhiteSpace(contextResult.Value)
            && !contextResult.Value.Contains("No graph context available.", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<SearchResult> WithTrace(IReadOnlyList<SearchResult> results, RetrievalTrace trace)
    {
        foreach (var result in results)
        {
            result.Trace = trace;
        }

        return results;
    }

    private sealed record TemporaryGraph(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);
}
