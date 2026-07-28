namespace Aletheia.RAGS.Abstractions.Models;

public sealed class ChatPlanRecord
{
    public Guid PlanId { get; init; }

    public string Prompt { get; init; } = string.Empty;

    public ChatExecutionMode Mode { get; init; }

    public ChatPlanStatus Status { get; init; } = ChatPlanStatus.Proposed;

    public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();

    public int EstimatedSecondsMin { get; init; }

    public int EstimatedSecondsMax { get; init; }

    public int EstimatedLlmCalls { get; init; }

    public int EstimatedInputTokens { get; init; }

    public int EstimatedOutputTokens { get; init; }

    public int EstimatedRetrievalCount { get; init; }

    public bool RequiresApproval { get; init; }

    public bool RequiresToolCall { get; init; }

    public string ToolName { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> ToolArguments { get; init; } = new Dictionary<string, string>();

    public string? ReviewedBy { get; init; }

    public DateTimeOffset? ReviewedAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAt { get; init; }

    public string? CancellationReason { get; init; }
}
