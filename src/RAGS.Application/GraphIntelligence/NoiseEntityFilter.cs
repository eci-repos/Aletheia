using Aletheia.RAGS.Abstractions.Interfaces;

namespace Aletheia.RAGS.Application.GraphIntelligence;

/// <summary>
/// Identifies "noise" entity types that must not be persisted to the graph.
/// <c>keyword</c> entities come from the entity-extraction LLM fallback
/// (<see cref="EntityExtractionService.SimpleExtractEntities"/>) and
/// <c>statistical-candidate</c> entities come from LazyGraphRAG query-time
/// statistical discovery. Both are retrieval-only signals, not real graph nodes.
/// </summary>
public static class NoiseEntityFilter
{
    private static readonly HashSet<string> NoiseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "keyword",
        "statistical-candidate",
    };

    public static bool IsNoise(ExtractedEntity entity)
        => entity is not null && NoiseTypes.Contains(entity.Type);

    public static bool IsNoise(string? type)
        => !string.IsNullOrWhiteSpace(type) && NoiseTypes.Contains(type);
}
