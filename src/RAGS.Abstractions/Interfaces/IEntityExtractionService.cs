using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IEntityExtractionService
{
    Task<Result<IReadOnlyList<ExtractedEntity>>> DiscoverAsync(string text, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ExtractedEntity>>> ClassifyAsync(IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ExtractedEntity>>> ScoreConfidenceAsync(IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default);
}

public sealed class ExtractedEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double Confidence { get; set; }
    public IReadOnlyDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
}
