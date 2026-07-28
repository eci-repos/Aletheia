using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IAgentService
{
    Task<Result<SummaryResponse>> SummarizeAsync(SummaryRequest request, CancellationToken cancellationToken = default);

    Task<Result<ExplanationResponse>> ExplainAsync(ExplanationRequest request, CancellationToken cancellationToken = default);

    Task<Result<DiscoveryResponse>> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken = default);
}
