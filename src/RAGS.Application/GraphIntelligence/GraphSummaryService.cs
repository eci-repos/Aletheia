using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;

namespace Aletheia.RAGS.Application.GraphIntelligence;

public sealed class GraphSummaryService : IGraphSummaryService
{
    private readonly Kernel _kernel;
    private readonly IGraphProvider _provider;
    private readonly ICommunityDetectionService _communityDetection;
    private readonly IAgentInstructionResolver? _instructionResolver;

    public GraphSummaryService(
        Kernel kernel,
        IGraphProvider provider,
        ICommunityDetectionService communityDetection,
        IAgentInstructionResolver? instructionResolver = null)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _communityDetection = communityDetection ?? throw new ArgumentNullException(nameof(communityDetection));
        _instructionResolver = instructionResolver;
    }

    public async Task<Result<string>> SummarizeEntityAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var nodeResult = await _provider.GetNodeAsync(entityId, cancellationToken).ConfigureAwait(false);
        if (nodeResult.IsFailure || nodeResult.Value is null)
        {
            return Result<string>.Failure($"Entity '{entityId}' not found in the graph.");
        }

        var node = nodeResult.Value;
        if (TryGetStoredSummary(node, out var storedSummary))
        {
            return Result<string>.Success(storedSummary);
        }

        var neighbors = await _provider.GetNeighborsAsync(entityId, cancellationToken).ConfigureAwait(false);
        var edges = await GetEntityEdgesAsync(entityId, cancellationToken).ConfigureAwait(false);

        var prompt = BuildEntitySummaryPrompt(node, neighbors.Value ?? Array.Empty<GraphNode>(), edges);
        var summary = await GenerateSummaryAsync(prompt, cancellationToken).ConfigureAwait(false);

        return Result<string>.Success(summary);
    }

    public async Task<Result<string>> SummarizeCommunityAsync(string communityId, CancellationToken cancellationToken = default)
    {
        var communityNodeResult = await _provider.GetNodeAsync(communityId, cancellationToken).ConfigureAwait(false);
        if (communityNodeResult.IsSuccess && communityNodeResult.Value is not null &&
            TryGetStoredSummary(communityNodeResult.Value, out var storedSummary))
        {
            return Result<string>.Success(storedSummary);
        }

        var communityResult = await _communityDetection.GetCommunityAsync(communityId, cancellationToken).ConfigureAwait(false);
        GraphCommunity? community = null;

        if (communityResult.IsSuccess && communityResult.Value is not null)
        {
            community = communityResult.Value;
        }
        else
        {
            // Fallback: discover communities and find the matching one
            var discovered = await _communityDetection.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            if (discovered.IsSuccess && discovered.Value is not null)
            {
                community = discovered.Value.FirstOrDefault(c => c.Id.Equals(communityId, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (community is null || !community.MemberIds.Any())
        {
            return Result<string>.Failure($"Community '{communityId}' not found or has no members.");
        }

        var memberNodes = new List<GraphNode>();
        foreach (var memberId in community.MemberIds)
        {
            var nodeResult = await _provider.GetNodeAsync(memberId, cancellationToken).ConfigureAwait(false);
            if (nodeResult.IsSuccess && nodeResult.Value is not null)
            {
                memberNodes.Add(nodeResult.Value);
            }
        }

        var prompt = BuildCommunitySummaryPrompt(community, memberNodes);
        var summary = await GenerateSummaryAsync(prompt, cancellationToken).ConfigureAwait(false);

        return Result<string>.Success(summary);
    }

    private static bool TryGetStoredSummary(GraphNode node, out string summary)
    {
        if (node.Properties.TryGetValue("summary", out var value) &&
            value is string stored &&
            !string.IsNullOrWhiteSpace(stored))
        {
            summary = stored;
            return true;
        }

        summary = string.Empty;
        return false;
    }

    public async Task<Result<string>> SummarizeClusterAsync(string clusterId, CancellationToken cancellationToken = default)
    {
        // Clusters are treated as communities in this implementation
        return await SummarizeCommunityAsync(clusterId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<string>> SummarizeGlobalAsync(CancellationToken cancellationToken = default)
    {
        var nodesResult = await _provider.GetNodesAsync(cancellationToken).ConfigureAwait(false);
        var edgesResult = await _provider.GetEdgesAsync(cancellationToken).ConfigureAwait(false);

        if (nodesResult.IsFailure || nodesResult.Value is null)
        {
            return Result<string>.Failure("Failed to retrieve graph nodes for global summary.");
        }

        var nodes = nodesResult.Value;
        var edges = edgesResult.IsSuccess && edgesResult.Value is not null ? edgesResult.Value : Array.Empty<GraphEdge>();

        var prompt = BuildGlobalSummaryPrompt(nodes, edges);
        var summary = await GenerateSummaryAsync(prompt, cancellationToken).ConfigureAwait(false);

        return Result<string>.Success(summary);
    }

    private async Task<IReadOnlyList<GraphEdge>> GetEntityEdgesAsync(string entityId, CancellationToken cancellationToken)
    {
        var edgesResult = await _provider.GetEdgesAsync(cancellationToken).ConfigureAwait(false);
        if (edgesResult.IsFailure || edgesResult.Value is null)
        {
            return Array.Empty<GraphEdge>();
        }

        return edgesResult.Value
            .Where(e => e.SourceId.Equals(entityId, StringComparison.OrdinalIgnoreCase)
                     || e.TargetId.Equals(entityId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string BuildEntitySummaryPrompt(GraphNode node, IReadOnlyList<GraphNode> neighbors, IReadOnlyList<GraphEdge> edges)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Entity: {node.Label}");
        sb.AppendLine($"Type: {node.Type}");

        if (node.Properties.Any())
        {
            sb.AppendLine("Properties:");
            foreach (var prop in node.Properties.Where(p => p.Value is not null))
            {
                sb.AppendLine($"  - {prop.Key}: {prop.Value}");
            }
        }

        if (neighbors.Any())
        {
            sb.AppendLine($"\nConnected Entities ({neighbors.Count}):");
            foreach (var neighbor in neighbors.Take(20))
            {
                sb.AppendLine($"  - {neighbor.Label} ({neighbor.Type})");
            }
        }

        if (edges.Any())
        {
            sb.AppendLine($"\nRelationships ({edges.Count}):");
            foreach (var edge in edges.Take(20))
            {
                sb.AppendLine($"  - {edge.RelationshipType}: {edge.SourceId} -> {edge.TargetId}");
            }
        }

        sb.AppendLine("\nGenerate a concise, factual summary of this entity based on the information above. Include its type, key properties, and significant relationships.");
        return sb.ToString();
    }

    private static string BuildCommunitySummaryPrompt(GraphCommunity community, IReadOnlyList<GraphNode> memberNodes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Community: {community.Name}");
        if (!string.IsNullOrWhiteSpace(community.Description))
        {
            sb.AppendLine($"Description: {community.Description}");
        }

        sb.AppendLine($"\nMembers ({memberNodes.Count}):");
        var typeGroups = memberNodes.GroupBy(n => n.Type).OrderByDescending(g => g.Count());
        foreach (var group in typeGroups)
        {
            sb.AppendLine($"  {group.Key} ({group.Count()}): {string.Join(", ", group.Select(n => n.Label).Take(10))}");
        }

        sb.AppendLine("\nGenerate a concise summary describing this community. What unifies these members? What are the key themes or functions?");
        return sb.ToString();
    }

    private static string BuildGlobalSummaryPrompt(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Graph Overview");
        sb.AppendLine($"Total Nodes: {nodes.Count}");
        sb.AppendLine($"Total Relationships: {edges.Count}");

        var typeGroups = nodes.GroupBy(n => n.Type).OrderByDescending(g => g.Count());
        sb.AppendLine("\nNode Types:");
        foreach (var group in typeGroups)
        {
            sb.AppendLine($"  - {group.Key}: {group.Count()}");
        }

        var relGroups = edges.GroupBy(e => e.RelationshipType).OrderByDescending(g => g.Count());
        sb.AppendLine("\nRelationship Types:");
        foreach (var group in relGroups.Take(10))
        {
            sb.AppendLine($"  - {group.Key}: {group.Count()}");
        }

        sb.AppendLine("\nGenerate a high-level summary of this knowledge graph. Describe the main domains, key entity types, and overall structure.");
        return sb.ToString();
    }

    private async Task<string> GenerateSummaryAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddSystemMessage(await ResolveSummarizerInstructionsAsync(cancellationToken).ConfigureAwait(false));
            history.AddUserMessage(prompt);

            var response = await chatCompletion.GetChatMessageContentAsync(history, cancellationToken: cancellationToken).ConfigureAwait(false);
            return response.Content?.Trim() ?? "Summary generation produced no content.";
        }
        catch
        {
            // Fallback: if LLM is unavailable, return a structured text summary
            return prompt.Replace("Generate a ", "Generated ").Replace("based on the information above", "from available data");
        }
    }

    /// <summary>Sprint 77: the summarizer system prompt resolves through <see cref="IAgentInstructionResolver"/>
    /// (<c>graphrag.summarizer</c>); the hard-coded prompt is the backward-compat fallback.</summary>
    private async Task<string> ResolveSummarizerInstructionsAsync(CancellationToken cancellationToken)
    {
        if (_instructionResolver is not null)
        {
            var resolved = await _instructionResolver
                .ResolveAsync(AgentInstructionRoles.GraphRagSummarizer, cancellationToken)
                .ConfigureAwait(false);
            if (resolved.IsSuccess && !string.IsNullOrWhiteSpace(resolved.Value!.Value))
            {
                return resolved.Value.Value;
            }
        }

        return "You are a precise knowledge graph summarization assistant. " +
               "Produce concise, factual summaries without speculation. Use only the provided data.";
    }
}
