using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;

namespace Aletheia.RAGS.Application.GraphRAG;

/// <summary>
/// Resolves a graph node's owning source id and filters nodes/communities to a theme scope.
/// Entity/chunk/relationship nodes carry <c>["sourceId"]</c> in <see cref="GraphNode.Properties"/>;
/// source nodes have <c>Type == "Source"</c> and <c>Id == sourceId.ToString()</c> with no
/// <c>sourceId</c> property. A null/empty allowlist means "no scope" (unfiltered).
/// </summary>
public static class GraphThemeScope
{
    /// <summary>
    /// Resolves the owning source id for a graph node, or null when the node is not attributable
    /// to a source (e.g. a synthetic/aggregate node).
    /// </summary>
    public static Guid? TryGetSourceId(GraphNode node)
    {
        if (node.Properties.TryGetValue("sourceId", out var raw))
        {
            if (raw is Guid guid)
            {
                return guid;
            }

            if (raw is string s && Guid.TryParse(s, out var parsed))
            {
                return parsed;
            }
        }

        if (string.Equals(node.Type, "Source", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(node.Id, out var sourceId))
        {
            return sourceId;
        }

        return null;
    }

    /// <summary>
    /// True when the node's source is in the allowlist, or when no scope is active.
    /// </summary>
    public static bool IsInScope(GraphNode node, IReadOnlySet<Guid>? allowedSourceIds)
    {
        if (allowedSourceIds is null || allowedSourceIds.Count == 0)
        {
            return true;
        }

        var sourceId = TryGetSourceId(node);
        return sourceId is not null && allowedSourceIds.Contains(sourceId.Value);
    }

    /// <summary>
    /// Filters nodes to those whose source is in the allowlist. A null/empty allowlist returns all nodes.
    /// </summary>
    public static IReadOnlyList<GraphNode> FilterNodes(
        IEnumerable<GraphNode> nodes,
        IReadOnlyList<Guid>? sourceIds)
    {
        if (sourceIds is null || sourceIds.Count == 0)
        {
            return nodes.ToList();
        }

        var allowed = sourceIds.ToHashSet();
        return nodes.Where(n => IsInScope(n, allowed)).ToList();
    }

    /// <summary>
    /// Normalizes a source-id allowlist to a set, or null when no scope is active.
    /// </summary>
    public static IReadOnlySet<Guid>? ToAllowSet(IReadOnlyList<Guid>? sourceIds)
        => sourceIds is null || sourceIds.Count == 0 ? null : sourceIds.ToHashSet();

    /// <summary>
    /// True when at least one of the community's member nodes belongs to the allowlist
    /// (match-any semantics, consistent with <c>KnowledgeThemeService.ResolveSourceIdsAsync</c>),
    /// or when no scope is active.
    /// </summary>
    public static bool CommunityHasMemberInScope(
        GraphCommunity community,
        IReadOnlyDictionary<string, Guid> nodeToSource,
        IReadOnlySet<Guid>? allowedSourceIds)
    {
        if (allowedSourceIds is null || allowedSourceIds.Count == 0)
        {
            return true;
        }

        return community.MemberIds.Any(memberId =>
            nodeToSource.TryGetValue(memberId, out var sourceId) && allowedSourceIds.Contains(sourceId));
    }
}
