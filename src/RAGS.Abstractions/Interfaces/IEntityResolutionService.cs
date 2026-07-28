using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IEntityResolutionService
{
    Task<Result<IReadOnlyList<EntityDuplicateGroup>>> DetectDuplicatesAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<EntityAliasGroup>>> DetectAliasesAsync(CancellationToken cancellationToken = default);

    Task<Result> ConsolidateAsync(string canonicalId, IReadOnlyList<string> duplicateIds, CancellationToken cancellationToken = default);
}

public sealed class EntityDuplicateGroup
{
    public string CanonicalId { get; set; } = string.Empty;
    public IReadOnlyList<string> DuplicateIds { get; set; } = Array.Empty<string>();
    public double SimilarityScore { get; set; }
}

public sealed class EntityAliasGroup
{
    public string CanonicalId { get; set; } = string.Empty;
    public IReadOnlyList<string> Aliases { get; set; } = Array.Empty<string>();
}
