using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IRelationshipExtractionService
{
    Task<Result<IReadOnlyList<ExtractedRelationship>>> DiscoverAsync(string text, IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ExtractedRelationship>>> ClassifyAsync(IReadOnlyList<ExtractedRelationship> relationships, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ExtractedRelationship>>> ScoreConfidenceAsync(IReadOnlyList<ExtractedRelationship> relationships, CancellationToken cancellationToken = default);
}

public sealed class ExtractedRelationship
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double Confidence { get; set; }
    public IReadOnlyDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
}
