namespace Aletheia.RAGS.Abstractions.Models;

public sealed class ChatJobSnapshot
{
    public Guid JobId { get; init; }

    public Guid PlanId { get; init; }

    public string Prompt { get; init; } = string.Empty;

    public ChatJobStatus Status { get; init; }

    public string Stage { get; init; } = string.Empty;

    public int PercentComplete { get; init; }

    public string Detail { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset LastHeartbeatAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? Result { get; init; }

    public string? Error { get; init; }
}
