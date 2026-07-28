namespace Aletheia.RAGS.Abstractions.Models;

public class SummaryResponse
{
    public string Summary { get; set; } = string.Empty;

    public IReadOnlyList<SearchResult> Sources { get; set; } = Array.Empty<SearchResult>();
}
