namespace Aletheia.RAGS.Abstractions.Models;

public sealed class GlobalSearchResult
{
    public GlobalSearchResult(string answer, IReadOnlyList<string> citations, IReadOnlyList<SearchResult> supportingResults)
    {
        Answer = answer ?? throw new ArgumentNullException(nameof(answer));
        Citations = citations ?? throw new ArgumentNullException(nameof(citations));
        SupportingResults = supportingResults ?? throw new ArgumentNullException(nameof(supportingResults));
    }

    public string Answer { get; }

    public IReadOnlyList<string> Citations { get; }

    public IReadOnlyList<SearchResult> SupportingResults { get; }
}
