using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.GraphIntelligence;
using Aletheia.RAGS.Application.LazyGraphRAG;
using Aletheia.RAGS.Application.Pipelines;
using Aletheia.RAGS.Application.Ranking;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Aletheia.RAGS.Application.GraphRAG;

public sealed class GraphRagService : IGraphRagService
{
    private const string RetrievalFailedMessage = "GraphRAG retrieval failed.";
    private const string SemanticTimeoutFallbackStrategy = "semantic-timeout-fallback";
    private const int MaxGraphChunks = 250;
    private const int MaxEntitiesPerChunk = 30;
    private const int MaxLlmConcurrency = 4;
    private const int MaxLazyChunksPerQuery = 3;
    private const int MaxLazyEntitiesPerChunk = 8;
    private const int MaxLazyEntitySummariesPerQuery = 4;
    private const int MaxLazyRelationshipsPerChunk = 8;

    // Sprint 62: when the per-request execution deadline fires, the best-effort semantic
    // fallback runs under its own short secondary deadline so a saturating LLM provider
    // cannot hang the degraded path indefinitely.
    private static readonly TimeSpan FallbackExecutionTime = TimeSpan.FromSeconds(10);

    private readonly IRagsService _ragsService;
    private readonly IGraphProvider _graphProvider;
    private readonly IEntityExtractionService _entityExtraction;
    private readonly IRelationshipExtractionService _relationshipExtraction;
    private readonly IGraphReasoningService _graphReasoning;
    private readonly IGraphSummaryService _graphSummary;
    private readonly IHierarchicalSummaryService _hierarchicalSummary;
    private readonly ICommunityDetectionService _communityDetection;
    private readonly IGraphContextBuilder _contextBuilder;
    private readonly ICitationPathService _citationPath;
    private readonly IGlobalGraphSearchService _globalSearch;
    private readonly ILazyEnrichmentKnowledgeSink? _knowledgeSink;
    private readonly ChunkingPipeline _chunkingPipeline;
    private readonly Func<IGraphTraversalBudget> _budgetFactory;

    public GraphRagService(
        IRagsService ragsService,
        IGraphProvider graphProvider,
        IEntityExtractionService entityExtraction,
        IRelationshipExtractionService relationshipExtraction,
        IGraphReasoningService graphReasoning,
        IGraphSummaryService graphSummary,
        IHierarchicalSummaryService hierarchicalSummary,
        ICommunityDetectionService communityDetection,
        IGraphContextBuilder contextBuilder,
        ICitationPathService citationPath,
        IGlobalGraphSearchService globalSearch,
        ILazyEnrichmentKnowledgeSink? knowledgeSink = null,
        ChunkingPipeline? chunkingPipeline = null,
        Func<IGraphTraversalBudget>? budgetFactory = null)
    {
        _ragsService = ragsService ?? throw new ArgumentNullException(nameof(ragsService));
        _graphProvider = graphProvider ?? throw new ArgumentNullException(nameof(graphProvider));
        _entityExtraction = entityExtraction ?? throw new ArgumentNullException(nameof(entityExtraction));
        _relationshipExtraction = relationshipExtraction ?? throw new ArgumentNullException(nameof(relationshipExtraction));
        _graphReasoning = graphReasoning ?? throw new ArgumentNullException(nameof(graphReasoning));
        _graphSummary = graphSummary ?? throw new ArgumentNullException(nameof(graphSummary));
        _hierarchicalSummary = hierarchicalSummary ?? throw new ArgumentNullException(nameof(hierarchicalSummary));
        _communityDetection = communityDetection ?? throw new ArgumentNullException(nameof(communityDetection));
        _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
        _citationPath = citationPath ?? throw new ArgumentNullException(nameof(citationPath));
        _globalSearch = globalSearch ?? throw new ArgumentNullException(nameof(globalSearch));
        _knowledgeSink = knowledgeSink;
        _chunkingPipeline = chunkingPipeline ?? new ChunkingPipeline();
        _budgetFactory = budgetFactory ?? (() => new GraphTraversalBudget());
    }

    public async Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
    {
        // Step 1: Standard RAG ingestion (chunks + embeddings)
        var ragsResult = await _ragsService.IngestAsync(request, cancellationToken).ConfigureAwait(false);
        if (ragsResult.IsFailure)
        {
            return ragsResult;
        }

        // Step 2: Create source document node in the graph
        var sourceNode = new GraphNode(
            request.SourceId.ToString(),
            request.SourceName ?? "chunk",
            "Source",
            new Dictionary<string, object>
            {
                ["content"] = request.Content,
            });

        // Sprint 63: gate community re-clustering — only re-run the O(graph) scan when this source is
        // new to the graph (first ingest). Re-ingests of an existing source skip it; retrieval-time
        // discovery still re-clusters on cache miss.
        var sourceExists = await SourceNodeExistsAsync(request.SourceId.ToString(), cancellationToken).ConfigureAwait(false);

        await _graphProvider.CreateNodeAsync(sourceNode, cancellationToken).ConfigureAwait(false);

        await PersistDocumentSummaryAsync(sourceNode, cancellationToken).ConfigureAwait(false);

        var chunks = _chunkingPipeline.Chunk(request.SourceId, request.Content).Take(MaxGraphChunks).ToList();

        // Phase 1: bounded-concurrency LLM extraction across chunks (entity + relationship discovery).
        // Chunks are independent, so they run in parallel up to MaxLlmConcurrency; within a chunk the
        // relationship pass depends on its entities, so those stay sequential.
        using var extractionSemaphore = new SemaphoreSlim(MaxLlmConcurrency);
        var extractionTasks = chunks.Select(async chunk =>
        {
            await extractionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var extractionResult = await _entityExtraction.DiscoverAsync(chunk.Content, null, cancellationToken).ConfigureAwait(false);
                if (extractionResult.IsFailure || extractionResult.Value is null || !extractionResult.Value.Any())
                {
                    return new ChunkExtraction(chunk, Array.Empty<ExtractedEntity>(), null);
                }

                var entities = extractionResult.Value
                    .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                    .Where(e => !NoiseEntityFilter.IsNoise(e))
                    .Take(MaxEntitiesPerChunk)
                    .Select(e => WithStableEntityId(e))
                    .ToList();

                var relationshipResult = await _relationshipExtraction
                    .DiscoverAsync(chunk.Content, entities, cancellationToken)
                    .ConfigureAwait(false);
                return new ChunkExtraction(chunk, entities, relationshipResult.IsSuccess ? relationshipResult.Value : null);
            }
            finally
            {
                extractionSemaphore.Release();
            }
        }).ToList();

        var extractions = await Task.WhenAll(extractionTasks).ConfigureAwait(false);

        // Phase 2: build all nodes/edges and write them in batched UNWIND statements instead of N+1
        // round-trips.
        var allNodes = new List<GraphNode>();
        var allEdges = new List<GraphEdge>();
        var entityNodes = new List<GraphNode>();
        var createdEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var extraction in extractions)
        {
            var chunk = extraction.Chunk;

            var chunkNode = new GraphNode(
                chunk.Id.ToString(),
                $"{request.SourceName ?? request.SourceId.ToString()} chunk {chunk.Index}",
                "Chunk",
                new Dictionary<string, object>
                {
                    ["sourceId"] = request.SourceId.ToString(),
                    ["sourceName"] = request.SourceName ?? string.Empty,
                    ["chunkIndex"] = chunk.Index,
                    ["content"] = chunk.Content
                });
            allNodes.Add(chunkNode);
            allEdges.Add(new GraphEdge(
                $"{request.SourceId}-has_chunk-{chunk.Id}",
                request.SourceId.ToString(),
                chunk.Id.ToString(),
                "has_chunk",
                new Dictionary<string, object>
                {
                    ["sourceId"] = request.SourceId.ToString(),
                    ["chunkIndex"] = chunk.Index
                }));

            foreach (var entity in extraction.Entities)
            {
                var entityNode = new GraphNode(
                    entity.Id,
                    entity.Name,
                    entity.Type,
                    new Dictionary<string, object>
                    {
                        ["confidence"] = entity.Confidence,
                        ["description"] = entity.Description ?? string.Empty,
                        ["sourceId"] = request.SourceId.ToString(),
                        ["sourceName"] = request.SourceName ?? string.Empty,
                        ["chunkId"] = chunk.Id.ToString(),
                        ["chunkIndex"] = chunk.Index
                    });
                allNodes.Add(entityNode);
                entityNodes.Add(entityNode);

                // Connect entity to source document
                allEdges.Add(new GraphEdge(
                    $"{entity.Id}-source-{request.SourceId}",
                    entity.Id,
                    request.SourceId.ToString(),
                    "found_in",
                    new Dictionary<string, object>
                    {
                        ["sourceId"] = request.SourceId.ToString(),
                        ["sourceName"] = request.SourceName ?? string.Empty,
                        ["summary"] = $"{entity.Name} was found in {request.SourceName ?? request.SourceId.ToString()}."
                    }));
                allEdges.Add(new GraphEdge(
                    $"{entity.Id}-mentioned_in-{chunk.Id}",
                    entity.Id,
                    chunk.Id.ToString(),
                    "mentioned_in",
                    new Dictionary<string, object>
                    {
                        ["sourceId"] = request.SourceId.ToString(),
                        ["chunkIndex"] = chunk.Index,
                        ["summary"] = $"{entity.Name} is mentioned in chunk {chunk.Index}."
                    }));
            }

            // Step 4: Extract relationships between entities at chunk granularity
            if (extraction.Relationships is not null)
            {
                foreach (var rel in extraction.Relationships)
                {
                    var sourceEntity = extraction.Entities.FirstOrDefault(e => e.Id.Equals(rel.SourceId, StringComparison.OrdinalIgnoreCase));
                    var targetEntity = extraction.Entities.FirstOrDefault(e => e.Id.Equals(rel.TargetId, StringComparison.OrdinalIgnoreCase));
                    var relationshipSummary = BuildRelationshipSummary(rel, sourceEntity, targetEntity, chunk.Index);
                    allEdges.Add(new GraphEdge(
                        StableRelationshipId(rel.SourceId, rel.TargetId, rel.Type, chunk.Id.ToString()),
                        rel.SourceId,
                        rel.TargetId,
                        NormalizeRelationshipType(rel.Type),
                        new Dictionary<string, object>
                        {
                            ["confidence"] = rel.Confidence,
                            ["description"] = rel.Description ?? string.Empty,
                            ["summary"] = relationshipSummary,
                            ["sourceId"] = request.SourceId.ToString(),
                            ["sourceName"] = request.SourceName ?? string.Empty,
                            ["chunkId"] = chunk.Id.ToString(),
                            ["chunkIndex"] = chunk.Index
                        }));
                }
            }
        }

        await _graphProvider.CreateNodesAsync(allNodes, cancellationToken).ConfigureAwait(false);
        await _graphProvider.CreateRelationshipsAsync(allEdges, cancellationToken).ConfigureAwait(false);

        // Phase 3: bounded-concurrency entity summaries (deduped across chunks).
        var newEntityNodes = entityNodes.Where(e => createdEntityIds.Add(e.Id)).ToList();
        using var summarySemaphore = new SemaphoreSlim(MaxLlmConcurrency);
        var summaryTasks = newEntityNodes.Select(async entityNode =>
        {
            await summarySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await PersistEntitySummaryAsync(entityNode, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                summarySemaphore.Release();
            }
        }).ToList();
        await Task.WhenAll(summaryTasks).ConfigureAwait(false);

        // Phase 4: gated community detection + bounded-concurrency community summaries.
        if (!sourceExists)
        {
            await PersistCommunitySummariesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }

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

        // Each request gets its own budget and a hard execution deadline so a single
        // slow LLM call cannot blow the budget or hang the request.
        var budget = _budgetFactory();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(budget.MaxExecutionTime);
        var ct = timeoutCts.Token;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var steps = new List<string>();
        var llmCalls = 0;

        try
        {
            // === Execution Trace: Query → Entity Resolution ===
            llmCalls++;
            var entityResolution = await _graphReasoning.SelectEntitiesAsync(query, ct).ConfigureAwait(false);
            var resolvedEntities = entityResolution.IsSuccess && entityResolution.Value is not null
                ? entityResolution.Value.ToList()
                : new List<GraphNode>();

            // Sprint 64: theme scope — drop entities outside the selected sources before community
            // resolution so summaries, communities, and citations stay within the scope.
            if (sourceIds is not null && sourceIds.Count > 0)
            {
                resolvedEntities = GraphThemeScope.FilterNodes(resolvedEntities, sourceIds).ToList();
            }
            steps.Add("entity-resolution");

            // === Execution Trace: Entity Resolution → Community Resolution ===
            var communityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in resolvedEntities.Take(5))
            {
                var communitiesResult = await _communityDetection.GetCommunitiesForNodeAsync(entity.Id, ct).ConfigureAwait(false);
                if (communitiesResult.IsSuccess && communitiesResult.Value is not null)
                {
                    foreach (var community in communitiesResult.Value)
                    {
                        communityIds.Add(community.Id);
                    }
                }
            }
            steps.Add("community-resolution");

            // === Execution Trace: Community Resolution → Summary Retrieval ===
            llmCalls += resolvedEntities.Take(3).Count() * 2;
            foreach (var entity in resolvedEntities.Take(3))
            {
                await _graphSummary.SummarizeEntityAsync(entity.Id, ct).ConfigureAwait(false);
                await _hierarchicalSummary.SummarizeEntityAsync(entity.Id, ct).ConfigureAwait(false);
            }

            llmCalls += communityIds.Take(3).Count() * 2;
            foreach (var communityId in communityIds.Take(3))
            {
                await _graphSummary.SummarizeCommunityAsync(communityId, ct).ConfigureAwait(false);
                await _hierarchicalSummary.SummarizeCommunityAsync(communityId, ct).ConfigureAwait(false);
            }
            steps.Add("summary-retrieval");

            // === Execution Trace: Summary Retrieval → Context Builder ===
            var contextResult = await _contextBuilder.BuildContextAsync(
                query,
                GraphContextSources.Entities | GraphContextSources.Communities | GraphContextSources.Summaries | GraphContextSources.Relationships,
                ct).ConfigureAwait(false);
            var contextScore = HasUsableContext(contextResult) ? 0.6f : 0f;
            steps.Add("context-build");

            var summaryCandidates = await BuildSummaryCandidatesAsync(
                query,
                resolvedEntities,
                communityIds,
                contextResult.Value,
                ct,
                sourceIds).ConfigureAwait(false);
            steps.Add("summary-candidates");

            if (summaryCandidates.Any())
            {
                var rankedSummaries = await GraphRagResultRanker.RankAndCiteAsync(
                    summaryCandidates,
                    _citationPath,
                    topK,
                    ct).ConfigureAwait(false);

                var summaryTrace = BuildTrace(
                    rankedSummaries.FirstOrDefault()?.RetrievalStrategy ?? "summary",
                    llmCalls,
                    budget,
                    steps,
                    stopwatch.ElapsedMilliseconds);

                return Result<IReadOnlyList<SearchResult>>.Success(WithTrace(rankedSummaries, summaryTrace));
            }

            llmCalls++;
            var lazyEntities = await EnsureQueryTimeEnrichmentAsync(query, topK, budget, ct, sourceIds).ConfigureAwait(false);
            if (lazyEntities.Count > 0)
            {
                steps.Add("lazy-enrichment");
                llmCalls++;
                var refreshedEntities = await _graphReasoning.SelectEntitiesAsync(query, ct).ConfigureAwait(false);
                resolvedEntities = refreshedEntities.IsSuccess && refreshedEntities.Value is not null
                    ? refreshedEntities.Value.ToList()
                    : new List<GraphNode>();

                foreach (var entity in lazyEntities)
                {
                    if (resolvedEntities.All(existing => !existing.Id.Equals(entity.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        resolvedEntities.Add(entity);
                    }
                }

                contextResult = await _contextBuilder.BuildContextAsync(
                    query,
                    GraphContextSources.Entities | GraphContextSources.Communities | GraphContextSources.Summaries | GraphContextSources.Relationships,
                    ct).ConfigureAwait(false);

                summaryCandidates = await BuildSummaryCandidatesAsync(
                    query,
                    resolvedEntities,
                    communityIds,
                    contextResult.Value,
                    ct,
                    sourceIds).ConfigureAwait(false);

                if (summaryCandidates.Any())
                {
                    var rankedLazySummaries = await GraphRagResultRanker.RankAndCiteAsync(
                        summaryCandidates,
                        _citationPath,
                        topK,
                        ct).ConfigureAwait(false);

                    var lazyTrace = BuildTrace(
                        "lazy-enrichment",
                        llmCalls,
                        budget,
                        steps,
                        stopwatch.ElapsedMilliseconds);

                    return Result<IReadOnlyList<SearchResult>>.Success(WithTrace(rankedLazySummaries, lazyTrace));
                }
            }

            // Step 1: Use Graph Reasoning Service for graph-aware retrieval
            llmCalls++;
            var reasoningResult = await _graphReasoning.RetrieveGraphAwareAsync(query, topK, ct).ConfigureAwait(false);
            if (reasoningResult.IsSuccess && reasoningResult.Value is not null && reasoningResult.Value.Any())
            {
                steps.Add("graph-aware");
                var graphAwareCandidates = reasoningResult.Value
                    .Select(r => new RetrievalCandidate(r, "graph-aware", graphScore: 0.85f, contextScore: contextScore));
                var ranked = await GraphRagResultRanker.RankAndCiteAsync(
                    graphAwareCandidates,
                    _citationPath,
                    topK * 2,
                    ct).ConfigureAwait(false);

                var graphAwareTrace = BuildTrace(
                    "graph-aware",
                    llmCalls,
                    budget,
                    steps,
                    stopwatch.ElapsedMilliseconds);

                return Result<IReadOnlyList<SearchResult>>.Success(WithTrace(ranked, graphAwareTrace));
            }

            // Step 2: Fall back to semantic-only retrieval if graph reasoning fails
            var baseResults = await _ragsService.RetrieveAsync(new RetrievalRequest(query, topK, sourceIds: sourceIds), ct).ConfigureAwait(false);
            if (baseResults.IsFailure || baseResults.Value is null)
            {
                return Result<IReadOnlyList<SearchResult>>.Failure(baseResults.Error ?? RetrievalFailedMessage);
            }

            var baseResultList = baseResults.Value.ToList();
            var expandedChunks = new HashSet<Guid>(baseResultList.Select(r => r.Chunk.Id));
            var candidates = baseResultList
                .Select(r => new RetrievalCandidate(r, "semantic", contextScore: contextScore))
                .ToList();
            steps.Add("semantic-retrieval");

            // Step 3: Entity-based multi-hop expansion
            var graphNodes = await _graphProvider.GetNodesAsync(ct).ConfigureAwait(false);
            if (graphNodes.IsSuccess && graphNodes.Value is not null && graphNodes.Value.Any())
            {
                // Sprint 64: theme scope — only traverse entities belonging to the selected sources.
                var scopedGraphNodes = GraphThemeScope.FilterNodes(graphNodes.Value, sourceIds);
                var entityNodes = scopedGraphNodes.Where(IsSemanticEntityNode).ToList();
                var queryEntities = FindQueryEntities(query, entityNodes);

                if (queryEntities.Any())
                {
                    var visitQueue = new Queue<(GraphNode Node, int Hops)>();
                    var visited = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                    foreach (var entity in queryEntities.Take(5))
                    {
                        visitQueue.Enqueue((entity, 0));
                        visited[entity.Id] = 0;
                    }

                    while (visitQueue.Count > 0)
                    {
                        var (current, hops) = visitQueue.Dequeue();
                        if (hops >= maxExpanded) continue;

                        var neighbors = await _graphProvider.GetNeighborsAsync(current.Id, ct).ConfigureAwait(false);
                        if (neighbors.IsSuccess && neighbors.Value is not null)
                        {
                            foreach (var neighbor in neighbors.Value)
                            {
                                if (!visited.ContainsKey(neighbor.Id))
                                {
                                    visited[neighbor.Id] = hops + 1;
                                    visitQueue.Enqueue((neighbor, hops + 1));
                                }
                            }
                        }
                    }

                    // Context expansion: retrieve chunks from expanded entity sources
                    var allGraphNodes = graphNodes.Value;
                    foreach (var (nodeId, hops) in visited.Take(topK * 2))
                    {
                        var node = allGraphNodes.FirstOrDefault(n => n.Id == nodeId);
                        if (node is null) continue;

                        var nodeResults = await _ragsService.RetrieveAsync(
                            new RetrievalRequest(node.Label, Math.Min(3, topK), sourceIds: sourceIds), ct).ConfigureAwait(false);

                        if (nodeResults.IsSuccess && nodeResults.Value is not null)
                        {
                            foreach (var result in nodeResults.Value)
                            {
                                if (!expandedChunks.Contains(result.Chunk.Id))
                                {
                                    expandedChunks.Add(result.Chunk.Id);
                                }

                                var graphScore = hops == 0 ? 0.9f : Math.Max(0.35f, 0.75f - (hops * 0.12f));
                                candidates.Add(new RetrievalCandidate(
                                    result,
                                    "graph-expansion",
                                    graphScore: graphScore,
                                    contextScore: contextScore));
                            }
                        }
                    }
                }
            }
            steps.Add("graph-expansion");

            var finalResults = await GraphRagResultRanker.RankAndCiteAsync(
                candidates,
                _citationPath,
                topK * 2,
                ct).ConfigureAwait(false);
            steps.Add("ranking");

            var finalTrace = BuildTrace(
                finalResults.FirstOrDefault()?.RetrievalStrategy ?? "semantic",
                llmCalls,
                budget,
                steps,
                stopwatch.ElapsedMilliseconds);

            return Result<IReadOnlyList<SearchResult>>.Success(WithTrace(finalResults, finalTrace));
        }
        catch (Exception ex)
        {
            // Sprint 62: a deadline cancellation is a soft signal, not a hard failure. When the
            // per-request execution deadline fires (but the caller did NOT cancel), degrade to a
            // best-effort plain semantic retrieval under a short secondary deadline and return the
            // best partial result with a visible timeout trace instead of failing the whole request.
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                steps.Add("deadline-exceeded");
                llmCalls++;
                try
                {
                    using var fallbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    fallbackCts.CancelAfter(FallbackExecutionTime);
                    var fallback = await _ragsService.RetrieveAsync(
                        new RetrievalRequest(query, topK, sourceIds: sourceIds), fallbackCts.Token).ConfigureAwait(false);

                    if (fallback.IsSuccess && fallback.Value is not null && fallback.Value.Any())
                    {
                        steps.Add("semantic-fallback");
                        var fallbackResults = fallback.Value.ToList();
                        var fallbackTrace = BuildTrace(
                            SemanticTimeoutFallbackStrategy,
                            llmCalls,
                            budget,
                            steps,
                            stopwatch.ElapsedMilliseconds);

                        return Result<IReadOnlyList<SearchResult>>.Success(WithTrace(fallbackResults, fallbackTrace));
                    }

                    return Result<IReadOnlyList<SearchResult>>.Failure(
                        fallback.Error ?? $"{RetrievalFailedMessage} The retrieval deadline was exceeded and the semantic fallback produced no results.");
                }
                catch (Exception fallbackEx)
                {
                    return Result<IReadOnlyList<SearchResult>>.Failure(
                        $"{RetrievalFailedMessage} The retrieval deadline was exceeded and the semantic fallback failed: {fallbackEx.Message}");
                }
            }

            // Caller cancellation and unexpected failures keep the hard-failure contract.
            return Result<IReadOnlyList<SearchResult>>.Failure(
                cancellationToken.IsCancellationRequested
                    ? $"{RetrievalFailedMessage} The operation was cancelled."
                    : $"{RetrievalFailedMessage} {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<GraphNode>> EnsureQueryTimeEnrichmentAsync(
        string query,
        int topK,
        IGraphTraversalBudget budget,
        CancellationToken cancellationToken,
        IReadOnlyList<Guid>? sourceIds = null)
    {
        var retrieval = await _ragsService.RetrieveAsync(
            new RetrievalRequest(query, Math.Clamp(topK, 1, MaxLazyChunksPerQuery), sourceIds: sourceIds),
            cancellationToken).ConfigureAwait(false);

        if (retrieval.IsFailure || retrieval.Value is null || retrieval.Value.Count == 0)
        {
            return Array.Empty<GraphNode>();
        }

        var enrichedNodes = new List<GraphNode>();
        var summarizedEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in retrieval.Value.Take(MaxLazyChunksPerQuery))
        {
            var chunk = result.Chunk;
            var existingChunk = await _graphProvider.GetNodeAsync(chunk.Id.ToString(), cancellationToken).ConfigureAwait(false);
            var sourceName = existingChunk.IsSuccess && existingChunk.Value is not null
                ? ResolveStringProperty(existingChunk.Value, "sourceName")
                : null;
            if (existingChunk.IsSuccess && existingChunk.Value is not null && IsTruthy(existingChunk.Value, "lazyEnriched"))
            {
                continue;
            }

            await EnsureLazyChunkSeedAsync(chunk, cancellationToken).ConfigureAwait(false);

            var extraction = await _entityExtraction.DiscoverAsync(chunk.Content, budget, cancellationToken).ConfigureAwait(false);
            if (extraction.IsFailure || extraction.Value is null || extraction.Value.Count == 0)
            {
                await MarkChunkLazyEnrichedAsync(chunk, "NoEntities", query, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var entities = extraction.Value
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .Where(e => !NoiseEntityFilter.IsNoise(e))
                .Take(MaxLazyEntitiesPerChunk)
                .Select(WithStableEntityId)
                .ToList();

            if (entities.Count == 0)
            {
                await MarkChunkLazyEnrichedAsync(chunk, "NoEntities", query, cancellationToken).ConfigureAwait(false);
                continue;
            }

            foreach (var entity in entities)
            {
                var entityNode = new GraphNode(
                    entity.Id,
                    entity.Name,
                    entity.Type,
                    new Dictionary<string, object>
                    {
                        ["confidence"] = entity.Confidence,
                        ["description"] = entity.Description ?? string.Empty,
                        ["sourceId"] = chunk.SourceId.ToString(),
                        ["sourceName"] = sourceName ?? string.Empty,
                        ["chunkId"] = chunk.Id.ToString(),
                        ["chunkIndex"] = chunk.Index,
                        ["lazyEnriched"] = true,
                        ["lazyEnrichmentQuery"] = query
                    });

                await _graphProvider.CreateNodeAsync(entityNode, cancellationToken).ConfigureAwait(false);
                if (enrichedNodes.All(existing => !existing.Id.Equals(entityNode.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    enrichedNodes.Add(entityNode);
                }

                await _graphProvider.CreateRelationshipAsync(
                    new GraphEdge(
                        $"{entity.Id}-source-{chunk.SourceId}",
                        entity.Id,
                        chunk.SourceId.ToString(),
                        "found_in",
                        new Dictionary<string, object>
                        {
                            ["sourceId"] = chunk.SourceId.ToString(),
                            ["sourceName"] = sourceName ?? string.Empty,
                            ["summary"] = $"{entity.Name} was discovered during lazy enrichment."
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
                            ["sourceId"] = chunk.SourceId.ToString(),
                            ["chunkIndex"] = chunk.Index,
                            ["summary"] = $"{entity.Name} is mentioned in chunk {chunk.Index}."
                        }),
                    cancellationToken).ConfigureAwait(false);

            if (summarizedEntities.Count < MaxLazyEntitySummariesPerQuery && summarizedEntities.Add(entity.Id))
            {
                await PersistEntitySummaryAsync(entityNode, cancellationToken).ConfigureAwait(false);
            }
        }

            var lazyRelationships = new List<ExtractedRelationship>();
            if (entities.Count > 1)
            {
                var relationships = await _relationshipExtraction.DiscoverAsync(
                    chunk.Content,
                    entities,
                    cancellationToken).ConfigureAwait(false);

                if (relationships.IsSuccess && relationships.Value is not null)
                {
                    lazyRelationships = relationships.Value.Take(MaxLazyRelationshipsPerChunk).ToList();
                    foreach (var relationship in lazyRelationships)
                    {
                        var sourceEntity = entities.FirstOrDefault(e => e.Id.Equals(relationship.SourceId, StringComparison.OrdinalIgnoreCase));
                        var targetEntity = entities.FirstOrDefault(e => e.Id.Equals(relationship.TargetId, StringComparison.OrdinalIgnoreCase));
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
                                    ["sourceId"] = chunk.SourceId.ToString(),
                                    ["sourceName"] = sourceName ?? string.Empty,
                                    ["chunkId"] = chunk.Id.ToString(),
                                    ["chunkIndex"] = chunk.Index,
                                    ["lazyEnriched"] = true
                                }),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            if (_knowledgeSink is not null)
            {
                await _knowledgeSink.RecordAsync(
                    chunk.SourceId,
                    sourceName,
                    entities,
                    lazyRelationships,
                    cancellationToken).ConfigureAwait(false);
            }

            await MarkChunkLazyEnrichedAsync(chunk, "Complete", query, cancellationToken).ConfigureAwait(false);
        }

        return enrichedNodes;
    }

    private async Task EnsureLazyChunkSeedAsync(Chunk chunk, CancellationToken cancellationToken)
    {
        await _graphProvider.CreateNodeAsync(
            new GraphNode(
                chunk.SourceId.ToString(),
                $"Source {chunk.SourceId}",
                "Source",
                new Dictionary<string, object>
                {
                    ["sourceId"] = chunk.SourceId.ToString(),
                    ["ingestionMode"] = "lazy-enrichment-seed"
                }),
            cancellationToken).ConfigureAwait(false);

        await _graphProvider.CreateNodeAsync(
            new GraphNode(
                chunk.Id.ToString(),
                $"Chunk {chunk.Index}",
                "Chunk",
                new Dictionary<string, object>
                {
                    ["sourceId"] = chunk.SourceId.ToString(),
                    ["chunkIndex"] = chunk.Index,
                    ["content"] = chunk.Content,
                    ["lazyEnriched"] = false,
                    ["lazyEnrichmentStatus"] = "Pending"
                }),
            cancellationToken).ConfigureAwait(false);

        await _graphProvider.CreateRelationshipAsync(
            new GraphEdge(
                $"{chunk.SourceId}-has_chunk-{chunk.Id}",
                chunk.SourceId.ToString(),
                chunk.Id.ToString(),
                "has_chunk",
                new Dictionary<string, object>
                {
                    ["sourceId"] = chunk.SourceId.ToString(),
                    ["chunkIndex"] = chunk.Index
                }),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkChunkLazyEnrichedAsync(
        Chunk chunk,
        string status,
        string query,
        CancellationToken cancellationToken)
    {
        var current = await _graphProvider.GetNodeAsync(chunk.Id.ToString(), cancellationToken).ConfigureAwait(false);
        var currentNode = current.IsSuccess ? current.Value : null;
        var properties = currentNode is not null
            ? new Dictionary<string, object>(currentNode.Properties)
            : new Dictionary<string, object>
            {
                ["sourceId"] = chunk.SourceId.ToString(),
                ["chunkIndex"] = chunk.Index,
                ["content"] = chunk.Content
            };

        properties["lazyEnriched"] = status.Equals("Complete", StringComparison.OrdinalIgnoreCase);
        properties["lazyEnrichmentStatus"] = status;
        properties["lazyEnrichedAt"] = DateTimeOffset.UtcNow.ToString("O");
        properties["lazyEnrichmentQuery"] = query;

        await _graphProvider.UpdateNodeAsync(
            new GraphNode(
                chunk.Id.ToString(),
                currentNode?.Label ?? $"Chunk {chunk.Index}",
                "Chunk",
                properties),
            cancellationToken).ConfigureAwait(false);
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

    private static List<GraphNode> FindQueryEntities(string query, List<GraphNode> entityNodes)
    {
        var result = new List<GraphNode>();
        var lowerQuery = query.ToLowerInvariant();

        foreach (var node in entityNodes)
        {
            if (lowerQuery.Contains(node.Label.ToLowerInvariant()))
            {
                result.Add(node);
            }
        }

        return result;
    }

    private static bool IsSemanticEntityNode(GraphNode node)
    {
        return !node.Type.Equals("Source", StringComparison.OrdinalIgnoreCase)
            && !node.Type.Equals("SourceDocument", StringComparison.OrdinalIgnoreCase)
            && !node.Type.Equals("Chunk", StringComparison.OrdinalIgnoreCase)
            && !node.Type.Equals("Community", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUsableContext(Result<string> contextResult)
    {
        return contextResult.IsSuccess
            && !string.IsNullOrWhiteSpace(contextResult.Value)
            && !contextResult.Value.Contains("No graph context available.", StringComparison.OrdinalIgnoreCase);
    }

    private static RetrievalTrace BuildTrace(
        string strategy,
        int llmCalls,
        IGraphTraversalBudget budget,
        IReadOnlyList<string> steps,
        long elapsedMs)
    {
        return new RetrievalTrace
        {
            Strategy = strategy,
            LlmCalls = llmCalls,
            TokensConsumed = budget.TokensConsumed,
            NodesVisited = budget.NodesVisited,
            RelationshipsTraversed = budget.RelationshipsTraversed,
            ElapsedMs = elapsedMs,
            Steps = steps,
        };
    }

    private static IReadOnlyList<SearchResult> WithTrace(IReadOnlyList<SearchResult> results, RetrievalTrace trace)
    {
        foreach (var result in results)
        {
            result.Trace = trace;
        }

        return results;
    }

    private static bool IsTruthy(GraphNode node, string propertyName)
    {
        if (!node.Properties.TryGetValue(propertyName, out var value) || value is null)
        {
            return false;
        }

        return value switch
        {
            bool boolean => boolean,
            string text => bool.TryParse(text, out var parsed) && parsed,
            _ => false
        };
    }

    private static string? ResolveStringProperty(GraphNode node, string propertyName)
    {
        return node.Properties.TryGetValue(propertyName, out var value)
            ? value?.ToString()
            : null;
    }

    private async Task PersistDocumentSummaryAsync(GraphNode sourceNode, CancellationToken cancellationToken)
    {
        var summary = await _hierarchicalSummary.SummarizeDocumentAsync(sourceNode.Id, cancellationToken).ConfigureAwait(false);
        if (summary.IsSuccess && !string.IsNullOrWhiteSpace(summary.Value))
        {
            await UpdateNodeSummaryAsync(sourceNode, summary.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PersistEntitySummaryAsync(GraphNode entityNode, CancellationToken cancellationToken)
    {
        var summary = await _graphSummary.SummarizeEntityAsync(entityNode.Id, cancellationToken).ConfigureAwait(false);
        if (summary.IsSuccess && !string.IsNullOrWhiteSpace(summary.Value))
        {
            await UpdateNodeSummaryAsync(entityNode, summary.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PersistCommunitySummariesAsync(CancellationToken cancellationToken)
    {
        var communities = await _communityDetection.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (communities.IsFailure || communities.Value is null)
        {
            return;
        }

        // Sprint 63: bounded-concurrency community summaries, written in one batched UNWIND update.
        var communityNodes = new List<GraphNode>();
        using var communitySemaphore = new SemaphoreSlim(MaxLlmConcurrency);
        var communityTasks = communities.Value.Take(50).Select(async community =>
        {
            await communitySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var summary = await _graphSummary.SummarizeCommunityAsync(community.Id, cancellationToken).ConfigureAwait(false);
                if (summary.IsFailure || string.IsNullOrWhiteSpace(summary.Value))
                {
                    return;
                }

                lock (communityNodes)
                {
                    communityNodes.Add(new GraphNode(
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
                        }));
                }
            }
            finally
            {
                communitySemaphore.Release();
            }
        }).ToList();
        await Task.WhenAll(communityTasks).ConfigureAwait(false);

        await _graphProvider.UpdateNodesAsync(communityNodes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SourceNodeExistsAsync(string sourceId, CancellationToken cancellationToken)
    {
        var result = await _graphProvider.GetNodeAsync(sourceId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess && result.Value is not null;
    }

    private sealed record ChunkExtraction(
        Chunk Chunk,
        IReadOnlyList<ExtractedEntity> Entities,
        IReadOnlyList<ExtractedRelationship>? Relationships);

    private async Task UpdateNodeSummaryAsync(GraphNode node, string summary, CancellationToken cancellationToken)
    {
        var properties = new Dictionary<string, object>(node.Properties)
        {
            ["summary"] = summary
        };

        await _graphProvider
            .UpdateNodeAsync(new GraphNode(node.Id, node.Label, node.Type, properties), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<RetrievalCandidate>> BuildSummaryCandidatesAsync(
        string query,
        IReadOnlyList<GraphNode> resolvedEntities,
        IReadOnlySet<string> communityIds,
        string? structuredContext,
        CancellationToken cancellationToken,
        IReadOnlyList<Guid>? sourceIds = null)
    {
        var candidates = new List<RetrievalCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var entities = resolvedEntities.Where(IsSemanticEntityNode).Take(8).ToList();
        if (entities.Count == 0)
        {
            var searched = await _graphProvider.SearchNodesAsync(query, cancellationToken).ConfigureAwait(false);
            if (searched.IsSuccess && searched.Value is not null)
            {
                entities = searched.Value.Where(IsSemanticEntityNode).Take(8).ToList();
            }
        }

        foreach (var entity in entities)
        {
            if (!seen.Add(entity.Id))
            {
                continue;
            }

            var summary = await _graphSummary.SummarizeEntityAsync(entity.Id, cancellationToken).ConfigureAwait(false);
            if (summary.IsFailure || string.IsNullOrWhiteSpace(summary.Value))
            {
                continue;
            }

            var citations = await ResolveEntityCitationsAsync(entity.Id, cancellationToken).ConfigureAwait(false);
            candidates.Add(CreateSummaryCandidate(
                entity.Id,
                ResolveSourceId(entity),
                $"Entity Summary: {entity.Label}\n{summary.Value}\n\n{structuredContext}",
                0.94f,
                "summary-entity",
                citations));
        }

        var communities = await ResolveRelevantCommunitiesAsync(query, communityIds, cancellationToken, sourceIds).ConfigureAwait(false);
        foreach (var community in communities.Take(6))
        {
            if (!seen.Add(community.Id))
            {
                continue;
            }

            var summary = await _graphSummary.SummarizeCommunityAsync(community.Id, cancellationToken).ConfigureAwait(false);
            if (summary.IsFailure || string.IsNullOrWhiteSpace(summary.Value))
            {
                continue;
            }

            var citations = await ResolveCommunityCitationsAsync(community, cancellationToken).ConfigureAwait(false);
            candidates.Add(CreateSummaryCandidate(
                community.Id,
                StableGuid("community-source", community.Id),
                $"Community Summary: {community.Name}\n{summary.Value}\n\n{structuredContext}",
                0.9f,
                "summary-community",
                citations));
        }

        return candidates;
    }

    private async Task<IReadOnlyList<GraphCommunity>> ResolveRelevantCommunitiesAsync(
        string query,
        IReadOnlySet<string> communityIds,
        CancellationToken cancellationToken,
        IReadOnlyList<Guid>? sourceIds = null)
    {
        var communities = new List<GraphCommunity>();
        foreach (var communityId in communityIds)
        {
            var community = await _communityDetection.GetCommunityAsync(communityId, cancellationToken).ConfigureAwait(false);
            if (community.IsSuccess && community.Value is not null)
            {
                communities.Add(community.Value);
            }
        }

        if (communities.Count > 0)
        {
            return communities;
        }

        var discovered = await _communityDetection.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (discovered.IsFailure || discovered.Value is null)
        {
            return Array.Empty<GraphCommunity>();
        }

        var queryTerms = Tokenize(query);
        var matched = discovered.Value
            .Where(c =>
                c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (c.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                queryTerms.Any(term => c.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (c.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)))
            .OrderByDescending(c => GetCommunityLevel(c))
            .ThenByDescending(c => c.MemberIds.Count)
            .Take(6)
            .ToList();

        // Sprint 64: theme scope — keep only communities whose members belong to the selected sources.
        if (sourceIds is not null && sourceIds.Count > 0)
        {
            var nodeToSource = await BuildNodeToSourceMapAsync(cancellationToken).ConfigureAwait(false);
            var allowed = GraphThemeScope.ToAllowSet(sourceIds);
            return matched
                .Where(c => GraphThemeScope.CommunityHasMemberInScope(c, nodeToSource, allowed))
                .ToList();
        }

        return matched;
    }

    private async Task<IReadOnlyDictionary<string, Guid>> BuildNodeToSourceMapAsync(CancellationToken cancellationToken)
    {
        var result = await _graphProvider.GetNodesAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsFailure || result.Value is null)
        {
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        }

        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in result.Value)
        {
            var sourceId = GraphThemeScope.TryGetSourceId(node);
            if (sourceId is not null)
            {
                map[node.Id] = sourceId.Value;
            }
        }

        return map;
    }

    private static RetrievalCandidate CreateSummaryCandidate(
        string id,
        Guid sourceId,
        string content,
        float score,
        string strategy,
        IReadOnlyList<string> citations)
    {
        var chunk = new Chunk(StableGuid("summary", id), sourceId, content, index: 0);
        return new RetrievalCandidate(
            new SearchResult(chunk, score, citations, retrievalStrategy: strategy),
            strategy,
            graphScore: 0.92f,
            contextScore: 0.85f,
            citationScore: citations.Count > 0 ? 1f : 0f);
    }

    private async Task<IReadOnlyList<string>> ResolveEntityCitationsAsync(string entityId, CancellationToken cancellationToken)
    {
        var citations = await _citationPath.GetEntitySourcesAsync(entityId, cancellationToken).ConfigureAwait(false);
        return citations.IsSuccess && citations.Value is not null ? citations.Value : Array.Empty<string>();
    }

    private async Task<IReadOnlyList<string>> ResolveCommunityCitationsAsync(GraphCommunity community, CancellationToken cancellationToken)
    {
        var citations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var memberId in community.MemberIds.Take(8))
        {
            var memberCitations = await _citationPath.GetEntitySourcesAsync(memberId, cancellationToken).ConfigureAwait(false);
            if (memberCitations.IsSuccess && memberCitations.Value is not null)
            {
                foreach (var citation in memberCitations.Value.Where(c => !string.IsNullOrWhiteSpace(c)))
                {
                    citations.Add(citation);
                }
            }
        }

        return citations.ToList();
    }

    private static Guid ResolveSourceId(GraphNode node)
    {
        if (node.Properties.TryGetValue("sourceId", out var sourceId) &&
            Guid.TryParse(sourceId?.ToString(), out var parsed) &&
            parsed != Guid.Empty)
        {
            return parsed;
        }

        return StableGuid("summary-source", node.Id);
    }

    private static IReadOnlyList<string> Tokenize(string value)
    {
        return Regex.Matches(value.ToLowerInvariant(), "[a-z0-9]{3,}")
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int GetCommunityLevel(GraphCommunity community)
    {
        if (community.Metadata.TryGetValue("level", out var level) &&
            int.TryParse(level?.ToString(), out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static ExtractedEntity WithStableEntityId(ExtractedEntity entity)
    {
        return new ExtractedEntity
        {
            Id = StableId("entity", entity.Type, entity.Name),
            Name = entity.Name,
            Type = string.IsNullOrWhiteSpace(entity.Type) ? "Entity" : entity.Type,
            Description = entity.Description,
            Confidence = entity.Confidence,
            Properties = entity.Properties
        };
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

    private static Guid StableGuid(string prefix, params string[] parts)
    {
        var key = string.Join("|", parts.Prepend(prefix).Select(p => p.Trim().ToLowerInvariant()));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(bytes.Take(16).ToArray());
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
