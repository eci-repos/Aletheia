using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;
using System.Text;

namespace Aletheia.RAGS.Application.GraphIntelligence;

public sealed class GraphContextBuilder : IGraphContextBuilder
{
    private readonly IGraphProvider _provider;
    private readonly ICommunityDetectionService _communityDetection;
    private readonly IGraphSummaryService _summaryService;

    public GraphContextBuilder(
        IGraphProvider provider,
        ICommunityDetectionService communityDetection,
        IGraphSummaryService summaryService)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _communityDetection = communityDetection ?? throw new ArgumentNullException(nameof(communityDetection));
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
    }

    public async Task<Result<string>> BuildContextAsync(string query, GraphContextSources sources, CancellationToken cancellationToken = default)
    {
        var sections = new List<(string Title, IReadOnlyList<string> Lines)>();

        if (sources.HasFlag(GraphContextSources.Documents))
        {
            var nodes = await _provider.GetNodesAsync(cancellationToken).ConfigureAwait(false);
            if (nodes.IsSuccess && nodes.Value is not null)
            {
                var sourcesNodes = nodes.Value
                    .Where(n => n.Type == "Source" && n.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Take(5)
                    .ToList();

                if (sourcesNodes.Any())
                {
                    sections.Add(("Documents", sourcesNodes.Select(n => $"- {n.Label}").ToList()));
                }
            }
        }

        if (sources.HasFlag(GraphContextSources.Entities))
        {
            var nodes = await _provider.SearchNodesAsync(query, cancellationToken).ConfigureAwait(false);
            if (nodes.IsSuccess && nodes.Value is not null && nodes.Value.Any())
            {
                var entityNodes = nodes.Value.Where(IsSemanticEntityNode).Take(10).ToList();
                if (entityNodes.Any())
                {
                    var lines = new List<string>();
                    foreach (var entity in entityNodes)
                    {
                        var summary = await _summaryService.SummarizeEntityAsync(entity.Id, cancellationToken).ConfigureAwait(false);
                        lines.Add(summary.IsSuccess && !string.IsNullOrWhiteSpace(summary.Value)
                            ? $"- {entity.Label} ({entity.Type}): {summary.Value}"
                            : $"- {entity.Label} ({entity.Type})");
                    }

                    sections.Add(("Entities", lines));
                }
            }
        }

        if (sources.HasFlag(GraphContextSources.Relationships))
        {
            var edges = await _provider.SearchRelationshipsAsync(query, cancellationToken).ConfigureAwait(false);
            if (edges.IsSuccess && edges.Value is not null && edges.Value.Any())
            {
                var lines = edges.Value
                    .Take(10)
                    .Select(edge =>
                    {
                        var summary = edge.Properties.TryGetValue("summary", out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
                            ? text
                            : $"{edge.SourceId} {edge.RelationshipType} {edge.TargetId}";
                        return $"- {edge.RelationshipType}: {summary}";
                    })
                    .ToList();

                sections.Add(("Relationships", lines));
            }
        }

        if (sources.HasFlag(GraphContextSources.Communities))
        {
            var communitiesResult = await _communityDetection.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            if (communitiesResult.IsSuccess && communitiesResult.Value is not null)
            {
                var allNodes = await _provider.GetNodesAsync(cancellationToken).ConfigureAwait(false);
                var matchingNodeIds = allNodes.IsSuccess && allNodes.Value is not null
                    ? allNodes.Value
                        .Where(n => n.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
                        .Select(n => n.Id)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var relevantCommunities = communitiesResult.Value
                    .Where(c => c.MemberIds.Any(m => matchingNodeIds.Contains(m)))
                    .Take(3)
                    .ToList();

                if (relevantCommunities.Any())
                {
                    var lines = new List<string>();
                    foreach (var community in relevantCommunities)
                    {
                        var summary = await _summaryService.SummarizeCommunityAsync(community.Id, cancellationToken).ConfigureAwait(false);
                        lines.Add(summary.IsSuccess && !string.IsNullOrWhiteSpace(summary.Value)
                            ? $"- {community.Name} ({community.MemberIds.Count} members): {summary.Value}"
                            : $"- {community.Name} ({community.MemberIds.Count} members)");
                    }

                    sections.Add(("Communities", lines));
                }
            }
        }

        if (sources.HasFlag(GraphContextSources.Summaries))
        {
            var nodes = await _provider.SearchNodesAsync(query, cancellationToken).ConfigureAwait(false);
            if (nodes.IsSuccess && nodes.Value is not null && nodes.Value.Any())
            {
                var topEntity = nodes.Value.FirstOrDefault(IsSemanticEntityNode) ?? nodes.Value.First();
                var summaryResult = await _summaryService.SummarizeEntityAsync(topEntity.Id, cancellationToken).ConfigureAwait(false);
                if (summaryResult.IsSuccess && !string.IsNullOrWhiteSpace(summaryResult.Value))
                {
                    sections.Add(("Best Summary", new[] { $"- {topEntity.Label}: {summaryResult.Value}" }));
                }
            }
        }

        if (!sections.Any())
        {
            return Result<string>.Success("No graph context available.");
        }

        return Result<string>.Success(BuildStructuredContext(query, sections));
    }

    private static string BuildStructuredContext(string query, IReadOnlyList<(string Title, IReadOnlyList<string> Lines)> sections)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Structured GraphRAG Context");
        sb.AppendLine($"Query: {query}");
        sb.AppendLine("Context Type: knowledge abstraction");

        foreach (var section in sections)
        {
            sb.AppendLine();
            sb.AppendLine(section.Title);
            foreach (var line in section.Lines)
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static bool IsSemanticEntityNode(GraphNode node)
    {
        return !node.Type.Equals("Source", StringComparison.OrdinalIgnoreCase)
            && !node.Type.Equals("SourceDocument", StringComparison.OrdinalIgnoreCase)
            && !node.Type.Equals("Chunk", StringComparison.OrdinalIgnoreCase)
            && !node.Type.Equals("Community", StringComparison.OrdinalIgnoreCase);
    }
}
