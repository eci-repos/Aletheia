using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface ICopilotService
{
    Task<Result<ChatMessage>> ChatAsync(
        ChatSession session,
        string userMessage,
        ChatRequestOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<Result<SummaryResponse>> SummarizeAsync(SummaryRequest request, CancellationToken cancellationToken = default);

    Task<Result<ExplanationResponse>> ExplainAsync(ExplanationRequest request, CancellationToken cancellationToken = default);

    Task<Result<DiscoveryResponse>> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken = default);
}
