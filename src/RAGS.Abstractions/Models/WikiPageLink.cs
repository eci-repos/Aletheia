namespace Aletheia.RAGS.Abstractions.Models;

public sealed record WikiPageLink(
    Guid Id,
    string Topic,
    string Title,
    string Status,
    int Version,
    DateTimeOffset UpdatedAt);
