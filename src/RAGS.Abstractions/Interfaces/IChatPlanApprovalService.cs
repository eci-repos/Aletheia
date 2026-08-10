using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IChatPlanApprovalService
{
    /// <summary>Creates a proposed plan. <paramref name="userId"/> (Sprint 61) is the caller's
    /// user id; when provided, the per-user <c>copilot.requireApproval</c> preference and the
    /// global admin override are applied to the plan's <see cref="ChatPlanRecord.RequiresApproval"/>.</summary>
    Task<Result<ChatPlanRecord>> CreatePlanAsync(string prompt, Guid? sessionId = null, IReadOnlyList<ChatMessage>? history = null, IReadOnlyList<string>? themeFilter = null, string? userId = null, CancellationToken cancellationToken = default);

    Task<Result<ChatPlanRecord>> ApproveAsync(Guid planId, string? approvedBy = null, CancellationToken cancellationToken = default);

    Task<Result<ChatPlanRecord>> CancelAsync(Guid planId, string? reason = null, CancellationToken cancellationToken = default);

    Task<Result<ChatPlanRecord?>> GetAsync(Guid planId, CancellationToken cancellationToken = default);
}
