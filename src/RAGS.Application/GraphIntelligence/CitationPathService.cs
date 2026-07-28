using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;

namespace Aletheia.RAGS.Application.GraphIntelligence;

public sealed class CitationPathService : ICitationPathService
{
    private readonly IGraphProvider _provider;

    public CitationPathService(IGraphProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<Result<IReadOnlyList<string>>> GetDocumentSourcesAsync(string resultId, CancellationToken cancellationToken = default)
    {
        var nodeResult = await _provider.GetNodeAsync(resultId, cancellationToken).ConfigureAwait(false);
        if (nodeResult.IsSuccess && nodeResult.Value is not null)
        {
            var sources = ExtractSourcesFromNode(nodeResult.Value);
            if (sources.Any())
            {
                return Result<IReadOnlyList<string>>.Success(sources);
            }
        }

        // Fallback: trace neighbors to Source nodes
        var neighbors = await _provider.GetNeighborsAsync(resultId, cancellationToken).ConfigureAwait(false);
        if (neighbors.IsSuccess && neighbors.Value is not null)
        {
            var sourceLabels = neighbors.Value
                .Where(n => n.Type == "Source")
                .Select(n => n.Label)
                .Distinct()
                .ToList();

            if (sourceLabels.Any())
            {
                return Result<IReadOnlyList<string>>.Success(sourceLabels);
            }
        }

        return Result<IReadOnlyList<string>>.Success(Array.Empty<string>());
    }

    public async Task<Result<IReadOnlyList<string>>> GetEntitySourcesAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var nodeResult = await _provider.GetNodeAsync(entityId, cancellationToken).ConfigureAwait(false);
        if (nodeResult.IsFailure || nodeResult.Value is null)
        {
            return Result<IReadOnlyList<string>>.Success(Array.Empty<string>());
        }

        var sources = ExtractSourcesFromNode(nodeResult.Value);
        if (sources.Any())
        {
            return Result<IReadOnlyList<string>>.Success(sources);
        }

        // Trace "found_in" edges to Source nodes
        var edgesResult = await _provider.GetEdgesAsync(cancellationToken).ConfigureAwait(false);
        if (edgesResult.IsSuccess && edgesResult.Value is not null)
        {
            var sourceIds = edgesResult.Value
                .Where(e => e.SourceId.Equals(entityId, StringComparison.OrdinalIgnoreCase)
                         && e.RelationshipType.Equals("found_in", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.TargetId)
                .Distinct()
                .ToList();

            var sourceLabels = new List<string>();
            foreach (var sourceId in sourceIds)
            {
                var sourceNode = await _provider.GetNodeAsync(sourceId, cancellationToken).ConfigureAwait(false);
                if (sourceNode.IsSuccess && sourceNode.Value is not null)
                {
                    sourceLabels.Add(sourceNode.Value.Label);
                }
            }

            if (sourceLabels.Any())
            {
                return Result<IReadOnlyList<string>>.Success(sourceLabels);
            }
        }

        return Result<IReadOnlyList<string>>.Success(Array.Empty<string>());
    }

    public async Task<Result<IReadOnlyList<string>>> GetRelationshipSourcesAsync(string relationshipId, CancellationToken cancellationToken = default)
    {
        var edgeResult = await _provider.GetRelationshipAsync(relationshipId, cancellationToken).ConfigureAwait(false);
        if (edgeResult.IsSuccess && edgeResult.Value is not null)
        {
            var edge = edgeResult.Value;

            if (edge.Properties.TryGetValue("sourceId", out var sourceId) && sourceId is string sid)
            {
                var nodeResult = await _provider.GetNodeAsync(sid, cancellationToken).ConfigureAwait(false);
                if (nodeResult.IsSuccess && nodeResult.Value is not null)
                {
                    return Result<IReadOnlyList<string>>.Success(new[] { nodeResult.Value.Label });
                }

                return Result<IReadOnlyList<string>>.Success(new[] { sid });
            }

            // Trace through source/target entities
            var sourceEntitySources = await GetEntitySourcesAsync(edge.SourceId, cancellationToken).ConfigureAwait(false);
            var targetEntitySources = await GetEntitySourcesAsync(edge.TargetId, cancellationToken).ConfigureAwait(false);

            var combined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sourceEntitySources.IsSuccess && sourceEntitySources.Value is not null)
            {
                foreach (var s in sourceEntitySources.Value) combined.Add(s);
            }
            if (targetEntitySources.IsSuccess && targetEntitySources.Value is not null)
            {
                foreach (var s in targetEntitySources.Value) combined.Add(s);
            }

            return Result<IReadOnlyList<string>>.Success(combined.ToList());
        }

        return Result<IReadOnlyList<string>>.Success(Array.Empty<string>());
    }

    public Task<Result<IReadOnlyList<GraphPath>>> GetGraphPathsAsync(string fromId, string toId, CancellationToken cancellationToken = default)
    {
        return _provider.FindPathsAsync(fromId, toId, cancellationToken);
    }

    private static IReadOnlyList<string> ExtractSourcesFromNode(GraphNode node)
    {
        var sources = new List<string>();

        if (node.Properties.TryGetValue("sourceId", out var sourceId) && sourceId is string sid && !string.IsNullOrWhiteSpace(sid))
        {
            sources.Add(sid);
        }

        if (node.Properties.TryGetValue("sourceName", out var sourceName) && sourceName is string sname && !string.IsNullOrWhiteSpace(sname))
        {
            sources.Add(sname);
        }

        return sources;
    }
}
