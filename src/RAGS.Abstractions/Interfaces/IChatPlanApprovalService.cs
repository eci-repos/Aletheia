using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IChatPlanApprovalService
{
    Task<Result<ChatPlanRecord>> CreatePlanAsync(string prompt, Guid? sessionId = null, IReadOnlyList<ChatMessage>? history = null, IReadOnlyList<string>? themeFilter = null, CancellationToken cancellationToken = default);

    Task<Result<ChatPlanRecord>> ApproveAsync(Guid planId, string? approvedBy = null, CancellationToken cancellationToken = default);

    Task<Result<ChatPlanRecord>> CancelAsync(Guid planId, string? reason = null, CancellationToken cancellationToken = default);

    Task<Result<ChatPlanRecord?>> GetAsync(Guid planId, CancellationToken cancellationToken = default);
}
