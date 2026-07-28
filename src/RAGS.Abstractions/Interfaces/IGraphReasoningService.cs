using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IGraphReasoningService
{
    Task<Result<IReadOnlyList<GraphPath>>> DiscoverReasoningPathsAsync(string query, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SearchResult>>> RetrieveGraphAwareAsync(string query, int topK, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphNode>>> SelectEntitiesAsync(string query, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphCommunity>>> SelectCommunitiesAsync(string query, CancellationToken cancellationToken = default);
}
