using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Application.Planning;

public sealed class ChatPlanApprovalService : IChatPlanApprovalService
{
    private readonly IChatPlanningService _planningService;
    private readonly IChatPlanRepository _repository;

    public ChatPlanApprovalService(
        IChatPlanningService planningService,
        IChatPlanRepository repository)
    {
        _planningService = planningService ?? throw new ArgumentNullException(nameof(planningService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<Result<ChatPlanRecord>> CreatePlanAsync(string prompt, Guid? sessionId = null, IReadOnlyList<ChatMessage>? history = null, CancellationToken cancellationToken = default)
    {
        var planResult = await _planningService.CreatePlanAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (planResult.IsFailure || planResult.Value is null)
        {
            return Result<ChatPlanRecord>.Failure(planResult.Error ?? "Failed to create execution plan.");
        }

        var plan = planResult.Value;
        var record = new ChatPlanRecord
        {
            PlanId = plan.PlanId,
            Prompt = plan.Prompt,
            Mode = plan.Mode,
            Status = ChatPlanStatus.Proposed,
            Steps = plan.Steps,
            EstimatedSecondsMin = plan.EstimatedSecondsMin,
            EstimatedSecondsMax = plan.EstimatedSecondsMax,
            EstimatedLlmCalls = plan.EstimatedLlmCalls,
            EstimatedInputTokens = plan.EstimatedInputTokens,
            EstimatedOutputTokens = plan.EstimatedOutputTokens,
            EstimatedRetrievalCount = plan.EstimatedRetrievalCount,
            RequiresApproval = plan.RequiresApproval,
            RequiresToolCall = plan.RequiresToolCall,
            ToolName = plan.ToolName,
            ToolArguments = plan.ToolArguments,
            CreatedAt = plan.CreatedAt,
            ExpiresAt = plan.ExpiresAt,
            SessionId = sessionId,
            HistoryMessages = history
        };

        var saveResult = await _repository.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        if (saveResult.IsFailure)
        {
            return Result<ChatPlanRecord>.Failure(saveResult.Error ?? "Failed to persist plan.");
        }

        return Result<ChatPlanRecord>.Success(record);
    }

    public async Task<Result<ChatPlanRecord>> ApproveAsync(Guid planId, string? approvedBy = null, CancellationToken cancellationToken = default)
    {
        var getResult = await _repository.GetAsync(planId, cancellationToken).ConfigureAwait(false);
        if (getResult.IsFailure)
        {
            return Result<ChatPlanRecord>.Failure(getResult.Error ?? "Failed to retrieve plan.");
        }

        var existing = getResult.Value;
        if (existing is null)
        {
            return Result<ChatPlanRecord>.Failure("Plan not found.");
        }

        if (existing.Status != ChatPlanStatus.Proposed)
        {
            return Result<ChatPlanRecord>.Failure($"Plan cannot be approved because it is {existing.Status}.");
        }

        if (existing.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            var expireResult = await _repository.UpdateStatusAsync(planId, ChatPlanStatus.Expired, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (expireResult.IsFailure)
            {
                return Result<ChatPlanRecord>.Failure(expireResult.Error ?? "Failed to expire plan.");
            }

            return Result<ChatPlanRecord>.Failure("Plan has expired.");
        }

        var updateResult = await _repository.UpdateStatusAsync(planId, ChatPlanStatus.Approved, approvedBy, cancellationToken).ConfigureAwait(false);
        if (updateResult.IsFailure)
        {
            return Result<ChatPlanRecord>.Failure(updateResult.Error ?? "Failed to approve plan.");
        }

        var approvedResult = await _repository.GetAsync(planId, cancellationToken).ConfigureAwait(false);
        return approvedResult.Value is not null
            ? Result<ChatPlanRecord>.Success(approvedResult.Value)
            : Result<ChatPlanRecord>.Failure("Plan disappeared after approval.");
    }

    public async Task<Result<ChatPlanRecord>> CancelAsync(Guid planId, string? reason = null, CancellationToken cancellationToken = default)
    {
        var getResult = await _repository.GetAsync(planId, cancellationToken).ConfigureAwait(false);
        if (getResult.IsFailure)
        {
            return Result<ChatPlanRecord>.Failure(getResult.Error ?? "Failed to retrieve plan.");
        }

        var existing = getResult.Value;
        if (existing is null)
        {
            return Result<ChatPlanRecord>.Failure("Plan not found.");
        }

        if (existing.Status != ChatPlanStatus.Proposed)
        {
            return Result<ChatPlanRecord>.Failure($"Plan cannot be cancelled because it is {existing.Status}.");
        }

        var cancelled = new ChatPlanRecord
        {
            PlanId = existing.PlanId,
            Prompt = existing.Prompt,
            Mode = existing.Mode,
            Status = ChatPlanStatus.Cancelled,
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
            CancellationReason = reason,
            ReviewedAt = DateTimeOffset.UtcNow,
            CreatedAt = existing.CreatedAt,
            ExpiresAt = existing.ExpiresAt
        };
        var saveResult = await _repository.SaveAsync(cancelled, cancellationToken).ConfigureAwait(false);
        if (saveResult.IsFailure)
        {
            return Result<ChatPlanRecord>.Failure(saveResult.Error ?? "Failed to cancel plan.");
        }

        return Result<ChatPlanRecord>.Success(cancelled);
    }

    public async Task<Result<ChatPlanRecord?>> GetAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var result = await _repository.GetAsync(planId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result<ChatPlanRecord?>.Failure(result.Error ?? "Failed to retrieve plan.");
        }

        var record = result.Value;
        if (record is not null && record.Status == ChatPlanStatus.Proposed && record.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await _repository.UpdateStatusAsync(planId, ChatPlanStatus.Expired, cancellationToken: cancellationToken).ConfigureAwait(false);
            var expiredResult = await _repository.GetAsync(planId, cancellationToken).ConfigureAwait(false);
            return Result<ChatPlanRecord?>.Success(expiredResult.Value);
        }

        return Result<ChatPlanRecord?>.Success(record);
    }
}
