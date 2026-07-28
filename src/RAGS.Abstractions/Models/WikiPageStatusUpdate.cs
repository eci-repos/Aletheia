namespace Aletheia.RAGS.Abstractions.Models;

public sealed record WikiPageStatusUpdate(string Status, string? ReviewedBy = null);
