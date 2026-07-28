namespace Aletheia.RAGS.Abstractions.Models;

public sealed class ChatProgressMessage
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string Stage { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}
