using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface ILazyEnrichmentKnowledgeSink
{
    Task<Result> RecordAsync(
        Guid sourceId,
        string? sourceName,
        IReadOnlyList<ExtractedEntity> entities,
        IReadOnlyList<ExtractedRelationship> relationships,
        CancellationToken cancellationToken = default);
}
