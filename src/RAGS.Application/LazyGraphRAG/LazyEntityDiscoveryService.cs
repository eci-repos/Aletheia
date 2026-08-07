using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Application.GraphIntelligence;

namespace Aletheia.RAGS.Application.LazyGraphRAG;

public sealed class LazyEntityDiscoveryService : ILazyEntityDiscoveryService
{
    private readonly IEntityExtractionService _extractionService;
    private readonly IGraphProvider _provider;

    public LazyEntityDiscoveryService(IEntityExtractionService extractionService, IGraphProvider provider)
    {
        _extractionService = extractionService ?? throw new ArgumentNullException(nameof(extractionService));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<Result<IReadOnlyList<ExtractedEntity>>> DiscoverAtQueryTimeAsync(string query, IGraphTraversalBudget? budget = null, CancellationToken cancellationToken = default)
    {
        var result = await _extractionService.DiscoverAsync(query, budget, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<Result<IReadOnlyList<ExtractedEntity>>> CreateIncrementalAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await _extractionService.DiscoverAsync(text, null, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<Result> PersistAsync(IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
        {
            // Noise entities (keyword / statistical-candidate) are retrieval-only signals,
            // never persisted as graph nodes.
            if (NoiseEntityFilter.IsNoise(entity))
            {
                continue;
            }

            var existing = await _provider.GetNodeAsync(entity.Id, cancellationToken).ConfigureAwait(false);
            var properties = new Dictionary<string, object>
            {
                ["confidence"] = entity.Confidence,
                ["description"] = entity.Description ?? string.Empty,
                ["discoveryMode"] = "lazy-query-time",
                ["discoveredAt"] = DateTimeOffset.UtcNow.ToString("O")
            };

            if (existing.IsSuccess && existing.Value is not null)
            {
                foreach (var property in existing.Value.Properties)
                {
                    properties.TryAdd(property.Key, property.Value);
                }

                var updatedNode = new GraphNode(
                    existing.Value.Id,
                    existing.Value.Label,
                    existing.Value.Type,
                    properties);

                var updateResult = await _provider.UpdateNodeAsync(updatedNode, cancellationToken).ConfigureAwait(false);
                if (updateResult.IsFailure)
                {
                    return updateResult;
                }

                continue;
            }

            var node = new GraphNode(
                entity.Id,
                entity.Name,
                string.IsNullOrWhiteSpace(entity.Type) ? "entity" : entity.Type,
                properties);

            var createResult = await _provider.CreateNodeAsync(node, cancellationToken).ConfigureAwait(false);
            if (createResult.IsFailure)
            {
                return createResult;
            }
        }

        return Result.Success();
    }
}
