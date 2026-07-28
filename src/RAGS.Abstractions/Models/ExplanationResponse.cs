namespace Aletheia.RAGS.Abstractions.Models;

public class ExplanationResponse
{
    public string Explanation { get; set; } = string.Empty;

    public IReadOnlyList<SearchResult> Sources { get; set; } = Array.Empty<SearchResult>();
}
