namespace Aletheia.RAGS.Abstractions.Models;

public sealed class WikiSearchRequest
{
    public string Query { get; init; } = string.Empty;

    public string Mode { get; init; } = "wrags";

    public int TopK { get; init; } = 6;

    public int Expansion { get; init; } = 1;

    public bool Regenerate { get; init; }
}
