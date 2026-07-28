using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IGraphSummaryService
{
    Task<Result<string>> SummarizeEntityAsync(string entityId, CancellationToken cancellationToken = default);

    Task<Result<string>> SummarizeCommunityAsync(string communityId, CancellationToken cancellationToken = default);

    Task<Result<string>> SummarizeClusterAsync(string clusterId, CancellationToken cancellationToken = default);

    Task<Result<string>> SummarizeGlobalAsync(CancellationToken cancellationToken = default);
}
