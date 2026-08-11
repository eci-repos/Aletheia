using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;

namespace Aletheia.RAGS.Application.GraphRAG;

public sealed class GlobalGraphSearchService : IGlobalGraphSearchService
{
    private readonly ICommunityDetectionService _communityDetection;
    private readonly IGraphSummaryService _graphSummary;
    private readonly IHierarchicalSummaryService _hierarchicalSummary;
    private readonly IGraphContextBuilder _contextBuilder;
    private readonly ICitationPathService _citationPath;
    private readonly Kernel? _kernel;
    private readonly IGraphProvider? _graphProvider;

    public GlobalGraphSearchService(
        ICommunityDetectionService communityDetection,
        IGraphSummaryService graphSummary,
        IHierarchicalSummaryService hierarchicalSummary,
        IGraphContextBuilder contextBuilder,
        ICitationPathService citationPath,
        Kernel? kernel = null,
        IGraphProvider? graphProvider = null)
    {
        _communityDetection = communityDetection ?? throw new ArgumentNullException(nameof(communityDetection));
        _graphSummary = graphSummary ?? throw new ArgumentNullException(nameof(graphSummary));
        _hierarchicalSummary = hierarchicalSummary ?? throw new ArgumentNullException(nameof(hierarchicalSummary));
        _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
        _citationPath = citationPath ?? throw new ArgumentNullException(nameof(citationPath));
        _kernel = kernel;
        _graphProvider = graphProvider;
    }

    public async Task<Result<GlobalSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default,
        IReadOnlyList<Guid>? sourceIds = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query is required.", nameof(query));
        }

        // 1. Community Detection
        var communitiesResult = await _communityDetection.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (communitiesResult.IsFailure || communitiesResult.Value is null || !communitiesResult.Value.Any())
        {
            return Result<GlobalSearchResult>.Failure("No communities detected in the graph.");
        }

        var communities = SelectTopLevelCommunities(communitiesResult.Value);

        // Sprint 64: theme scope — keep only communities whose members belong to the selected sources.
        if (sourceIds is not null && sourceIds.Count > 0)
        {
            communities = await FilterCommunitiesToScopeAsync(communities, sourceIds, cancellationToken).ConfigureAwait(false);
            if (!communities.Any())
            {
                return Result<GlobalSearchResult>.Failure("No communities in the selected themes.");
            }
        }

        // 2. Summary Retrieval (Map Phase)
        var communitySummaries = new List<(GraphCommunity Community, string Summary)>();
        foreach (var community in communities)
        {
            var summaryResult = await _graphSummary.SummarizeCommunityAsync(community.Id, cancellationToken).ConfigureAwait(false);
            if (summaryResult.IsSuccess && !string.IsNullOrWhiteSpace(summaryResult.Value))
            {
                communitySummaries.Add((community, summaryResult.Value));
            }
        }

        if (!communitySummaries.Any())
        {
            return Result<GlobalSearchResult>.Failure("No community summaries are available for global search.");
        }

        // 3. Structured Context Builder
        var contextResult = await _contextBuilder.BuildContextAsync(
            query,
            GraphContextSources.Communities | GraphContextSources.Summaries | GraphContextSources.Entities,
            cancellationToken).ConfigureAwait(false);

        // 4. Map-reduce synthesis over top-level community summaries
        var mappedAnswers = new List<(GraphCommunity Community, string Answer)>();
        foreach (var (community, summary) in communitySummaries)
        {
            var mapped = await MapCommunityAsync(query, community, summary, cancellationToken).ConfigureAwait(false);
            mappedAnswers.Add((community, mapped));
        }

        var synthesis = await ReduceAsync(query, mappedAnswers, contextResult.Value, cancellationToken).ConfigureAwait(false);

        // 5. Citation Builder
        var citations = new List<string>();
        foreach (var (community, _) in communitySummaries)
        {
            foreach (var memberId in community.MemberIds.Take(5))
            {
                var entitySources = await _citationPath.GetEntitySourcesAsync(memberId, cancellationToken).ConfigureAwait(false);
                if (entitySources.IsSuccess && entitySources.Value is not null)
                {
                    citations.AddRange(entitySources.Value);
                }
            }
        }

        var distinctCitations = citations.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var result = new GlobalSearchResult(
            synthesis,
            distinctCitations,
            Array.Empty<SearchResult>());

        return Result<GlobalSearchResult>.Success(result);
    }

    private async Task<string> MapCommunityAsync(
        string query,
        GraphCommunity community,
        string summary,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Map Step");
        sb.AppendLine($"Query: {query}");
        sb.AppendLine($"Community: {community.Name}");
        sb.AppendLine($"Members: {community.MemberIds.Count}");
        sb.AppendLine($"Summary: {summary}");
        sb.AppendLine();
        sb.AppendLine("Answer the query using only this community summary. Be concise and cite the community name in the answer.");

        return await CompleteOrReturnPromptAsync(
            sb.ToString(),
            "You are the map phase of a GraphRAG global search. Extract only the facts relevant to the query from one community summary.",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ReduceAsync(
        string query,
        IReadOnlyList<(GraphCommunity Community, string Answer)> mappedAnswers,
        string? structuredContext,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Reduce Step");
        sb.AppendLine($"Query: {query}");
        sb.AppendLine("Input Type: mapped top-level community summary answers");
        sb.AppendLine();

        foreach (var (community, answer) in mappedAnswers.Take(20))
        {
            sb.AppendLine($"Community: {community.Name}");
            sb.AppendLine($"Mapped Answer: {answer}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(structuredContext))
        {
            sb.AppendLine("Structured Context:");
            sb.AppendLine(structuredContext);
            sb.AppendLine();
        }

        sb.AppendLine("Reduce the mapped answers into a concise corpus-level answer. Emphasize broad themes, cross-community patterns, and uncertainty where summaries are thin.");

        return await CompleteOrReturnPromptAsync(
            sb.ToString(),
            "You are the reduce phase of a GraphRAG global search. Synthesize mapped community answers into one factual corpus-level response.",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> CompleteOrReturnPromptAsync(
        string prompt,
        string systemMessage,
        CancellationToken cancellationToken)
    {
        if (_kernel is null)
        {
            return prompt;
        }

        try
        {
            var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddSystemMessage(systemMessage);
            history.AddUserMessage(prompt);

            var response = await chatCompletion.GetChatMessageContentAsync(history, cancellationToken: cancellationToken).ConfigureAwait(false);
            return response.Content?.Trim() ?? prompt;
        }
        catch
        {
            return prompt;
        }
    }

    private async Task<IReadOnlyList<GraphCommunity>> FilterCommunitiesToScopeAsync(
        IReadOnlyList<GraphCommunity> communities,
        IReadOnlyList<Guid> sourceIds,
        CancellationToken cancellationToken)
    {
        if (_graphProvider is null)
        {
            return communities;
        }

        var nodesResult = await _graphProvider.GetNodesAsync(cancellationToken).ConfigureAwait(false);
        if (nodesResult.IsFailure || nodesResult.Value is null)
        {
            return communities;
        }

        var nodeToSource = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodesResult.Value)
        {
            var sourceId = GraphThemeScope.TryGetSourceId(node);
            if (sourceId is not null)
            {
                nodeToSource[node.Id] = sourceId.Value;
            }
        }

        var allowed = GraphThemeScope.ToAllowSet(sourceIds);
        return communities
            .Where(c => GraphThemeScope.CommunityHasMemberInScope(c, nodeToSource, allowed))
            .ToList();
    }

    private static IReadOnlyList<GraphCommunity> SelectTopLevelCommunities(IReadOnlyList<GraphCommunity> communities)
    {
        var maxLevel = communities.Max(GetCommunityLevel);
        var topLevel = communities
            .Where(c => GetCommunityLevel(c) == maxLevel)
            .OrderByDescending(c => c.MemberIds.Count)
            .ToList();

        return topLevel.Count > 0
            ? topLevel
            : communities.OrderByDescending(c => c.MemberIds.Count).ToList();
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
}
