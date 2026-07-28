namespace Aletheia.RAGS.Abstractions.Models;

public sealed record WikiPageHistoryEntry(
    Guid Id,
    Guid PageId,
    int Version,
    string Title,
    string Summary,
    string Status,
    IReadOnlyList<string> RelatedTopics,
    string ChangeType,
    string? ChangedBy,
    string? ChangeNote,
    DateTimeOffset CreatedAt);
