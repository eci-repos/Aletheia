namespace Aletheia.RAGS.Abstractions.Models;

public sealed class ChatProgressStep
{
    public string Name { get; init; } = string.Empty;

    public ChatProgressStepStatus Status { get; init; }

    public int Order { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? Detail { get; init; }
}

public enum ChatProgressStepStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}
