using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IChatPlanRepository
{
    Task<Result> SaveAsync(ChatPlanRecord plan, CancellationToken cancellationToken = default);

    Task<Result<ChatPlanRecord?>> GetAsync(Guid planId, CancellationToken cancellationToken = default);

    Task<Result> UpdateStatusAsync(
        Guid planId,
        ChatPlanStatus status,
        string? reviewedBy = null,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ChatPlanRecord>>> GetPendingAsync(CancellationToken cancellationToken = default);
}
