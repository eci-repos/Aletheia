namespace Aletheia.RAGS.Abstractions.Models;

public sealed class ChatProgressHeartbeat
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string Stage { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public int PercentComplete { get; init; }
}
