using System.Collections.Concurrent;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Application.Planning;

public sealed class InMemoryChatPlanRepository : IChatPlanRepository
{
    private readonly ConcurrentDictionary<Guid, ChatPlanRecord> _plans = new();

    public Task<Result> SaveAsync(ChatPlanRecord plan, CancellationToken cancellationToken = default)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        _plans[plan.PlanId] = plan;
        return Task.FromResult(Result.Success());
    }

    public Task<Result<ChatPlanRecord?>> GetAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        _plans.TryGetValue(planId, out var plan);
        return Task.FromResult(Result<ChatPlanRecord?>.Success(plan));
    }

    public Task<Result> UpdateStatusAsync(
        Guid planId,
        ChatPlanStatus status,
        string? reviewedBy = null,
        CancellationToken cancellationToken = default)
    {
        if (!_plans.TryGetValue(planId, out var existing))
        {
            return Task.FromResult(Result.Failure("Plan not found."));
        }

        var updated = new ChatPlanRecord
        {
            PlanId = existing.PlanId,
            Prompt = existing.Prompt,
            Mode = existing.Mode,
            Status = status,
            Steps = existing.Steps,
            EstimatedSecondsMin = existing.EstimatedSecondsMin,
            EstimatedSecondsMax = existing.EstimatedSecondsMax,
            EstimatedLlmCalls = existing.EstimatedLlmCalls,
            EstimatedInputTokens = existing.EstimatedInputTokens,
            EstimatedOutputTokens = existing.EstimatedOutputTokens,
            EstimatedRetrievalCount = existing.EstimatedRetrievalCount,
            RequiresApproval = existing.RequiresApproval,
            RequiresToolCall = existing.RequiresToolCall,
            ToolName = existing.ToolName,
            ToolArguments = existing.ToolArguments,
            SessionId = existing.SessionId,
            HistoryMessages = existing.HistoryMessages,
            ThemeFilter = existing.ThemeFilter,
            ReviewedBy = reviewedBy ?? existing.ReviewedBy,
            ReviewedAt = status is ChatPlanStatus.Approved or ChatPlanStatus.Cancelled
                ? DateTimeOffset.UtcNow
                : existing.ReviewedAt,
            CreatedAt = existing.CreatedAt,
            ExpiresAt = existing.ExpiresAt
        };

        _plans[planId] = updated;
        return Task.FromResult(Result.Success());
    }

    public Task<Result<IReadOnlyList<ChatPlanRecord>>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        var pending = _plans.Values
            .Where(plan => plan.Status == ChatPlanStatus.Proposed && plan.ExpiresAt > DateTimeOffset.UtcNow)
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<ChatPlanRecord>>.Success(pending));
    }
}
