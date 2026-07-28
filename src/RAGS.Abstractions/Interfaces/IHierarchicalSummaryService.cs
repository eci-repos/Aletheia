using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IHierarchicalSummaryService
{
    Task<Result<string>> SummarizeDocumentAsync(string documentId, CancellationToken cancellationToken = default);

    Task<Result<string>> SummarizeEntityAsync(string entityId, CancellationToken cancellationToken = default);

    Task<Result<string>> SummarizeCommunityAsync(string communityId, CancellationToken cancellationToken = default);

    Task<Result<string>> SummarizeKnowledgeAreaAsync(string areaId, CancellationToken cancellationToken = default);

    Task<Result<string>> SummarizeGlobalAsync(CancellationToken cancellationToken = default);
}
