using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Application.GraphRAG;

/// <summary>
/// Computes knowledge-graph summary coverage from the graph itself (ground truth), not from job
/// history. A community node with a stored <c>summary</c> property is "summarized"; entity nodes
/// carry the <c>sourceId</c> they belong to; <c>has_member</c> edges map communities to their
/// member entities (and therefore to sources). A community counts toward every source it touches.
/// </summary>
public sealed class SummariesStatusService : ISummariesStatusService
{
    private const string SourceType = "Source";
    private const string CommunityType = "Community";
    private const string ChunkType = "Chunk";
    private const string HasMemberRelationship = "has_member";
    private const string SourceIdProperty = "sourceId";
    private const string SourceNameProperty = "sourceName";
    private const string SummaryProperty = "summary";

    private readonly IGraphProvider _provider;

    public SummariesStatusService(IGraphProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<Result<SummariesStatusSnapshot>> GetAsync(CancellationToken cancellationToken = default)
    {
        var nodesResult = await _provider.GetNodesAsync(cancellationToken).ConfigureAwait(false);
        if (nodesResult.IsFailure || nodesResult.Value is null)
        {
            return Result<SummariesStatusSnapshot>.Failure("Failed to read graph nodes.");
        }

        var nodes = nodesResult.Value;
        if (nodes.Count == 0)
        {
            return Result<SummariesStatusSnapshot>.Success(new SummariesStatusSnapshot());
        }

        var edgesResult = await _provider.GetEdgesAsync(cancellationToken).ConfigureAwait(false);
        var edges = edgesResult.IsSuccess && edgesResult.Value is not null
            ? edgesResult.Value
            : Array.Empty<GraphEdge>();

        var sourceNodes = nodes.Where(n => n.Type.Equals(SourceType, StringComparison.OrdinalIgnoreCase)).ToList();
        var communityNodes = nodes.Where(n => n.Type.Equals(CommunityType, StringComparison.OrdinalIgnoreCase)).ToList();
        var entityNodes = nodes
            .Where(n => !n.Type.Equals(SourceType, StringComparison.OrdinalIgnoreCase)
                     && !n.Type.Equals(CommunityType, StringComparison.OrdinalIgnoreCase)
                     && !n.Type.Equals(ChunkType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var sourceNames = sourceNodes.ToDictionary(
            n => n.Id,
            n => GetPropertyString(n, SourceNameProperty) ?? n.Label,
            StringComparer.OrdinalIgnoreCase);

        // Entity node id -> source id (entities are the members that carry a source).
        var entitySourceIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entityNodes)
        {
            var sourceId = GetPropertyString(entity, SourceIdProperty);
            if (!string.IsNullOrWhiteSpace(sourceId))
            {
                entitySourceIds[entity.Id] = sourceId;
            }
        }

        // Community id -> node, for summary lookups.
        var communityById = communityNodes.ToDictionary(
            n => n.Id,
            n => n,
            StringComparer.OrdinalIgnoreCase);

        var perSource = new Dictionary<string, SourceSummaryStatus>(StringComparer.OrdinalIgnoreCase);
        var communitySets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var summarizedCommunitySets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entityNodes)
        {
            if (!entitySourceIds.TryGetValue(entity.Id, out var sourceId))
            {
                continue;
            }

            var status = GetOrAddSource(perSource, sourceId, sourceNames);
            status.EntityCount++;
        }

        foreach (var edge in edges)
        {
            if (!edge.RelationshipType.Equals(HasMemberRelationship, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // has_member edges are community -> member entity.
            if (!entitySourceIds.TryGetValue(edge.TargetId, out var sourceId))
            {
                continue;
            }

            if (!communityById.TryGetValue(edge.SourceId, out var communityNode))
            {
                continue;
            }

            var status = GetOrAddSource(perSource, sourceId, sourceNames);
            if (!communitySets.TryGetValue(sourceId, out var communities))
            {
                communities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                communitySets[sourceId] = communities;
            }

            if (communities.Add(communityNode.Id))
            {
                status.CommunityCount++;
            }

            if (HasSummary(communityNode))
            {
                if (!summarizedCommunitySets.TryGetValue(sourceId, out var summarized))
                {
                    summarized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    summarizedCommunitySets[sourceId] = summarized;
                }

                if (summarized.Add(communityNode.Id))
                {
                    status.SummarizedCommunityCount++;
                }
            }
        }

        var snapshot = new SummariesStatusSnapshot
        {
            GraphExists = true,
            NodeCount = nodes.Count,
            EntityCount = entityNodes.Count,
            CommunityCount = communityNodes.Count,
            SummarizedCommunityCount = communityNodes.Count(HasSummary),
            SourceCount = sourceNodes.Count,
            Sources = perSource.Values
                .OrderBy(s => s.SourceName, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        return Result<SummariesStatusSnapshot>.Success(snapshot);
    }

    private static SourceSummaryStatus GetOrAddSource(
        Dictionary<string, SourceSummaryStatus> perSource,
        string sourceId,
        IReadOnlyDictionary<string, string> sourceNames)
    {
        if (perSource.TryGetValue(sourceId, out var existing))
        {
            return existing;
        }

        var status = new SourceSummaryStatus
        {
            SourceId = Guid.TryParse(sourceId, out var parsed) ? parsed : Guid.Empty,
            SourceName = sourceNames.TryGetValue(sourceId, out var name) ? name : sourceId
        };
        perSource[sourceId] = status;
        return status;
    }

    private static bool HasSummary(GraphNode node) =>
        !string.IsNullOrWhiteSpace(GetPropertyString(node, SummaryProperty));

    private static string? GetPropertyString(GraphNode node, string key) =>
        node.Properties.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
}
