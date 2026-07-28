namespace Aletheia.RAGS.Abstractions.Models;

public record ChatProgressRecord
{
    public Guid JobId { get; init; }

    public Guid PlanId { get; init; }

    public string Prompt { get; init; } = string.Empty;

    public ChatJobStatus Status { get; init; }

    public IReadOnlyList<ChatProgressStep> Steps { get; init; } = Array.Empty<ChatProgressStep>();

    public IReadOnlyList<ChatProgressHeartbeat> Heartbeats { get; init; } = Array.Empty<ChatProgressHeartbeat>();

    public IReadOnlyList<ChatProgressMessage> Messages { get; init; } = Array.Empty<ChatProgressMessage>();

    public string? PartialResult { get; init; }

    public string? FinalResult { get; init; }

    public string? Error { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public int PercentComplete { get; init; }

    public ChatExecutionTelemetry? Telemetry { get; init; }
}
