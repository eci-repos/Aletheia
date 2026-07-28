namespace Aletheia.Repository.Abstractions.Models;

public sealed class SearchRequest
{
    public SearchRequest(
        string? query,
        int pageNumber,
        int pageSize,
        IReadOnlyDictionary<string, string>? filters = null)
    {
        if (pageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page number must be greater than zero.");
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be greater than zero.");
        }

        Query = string.IsNullOrWhiteSpace(query) ? null : query;
        PageNumber = pageNumber;
        PageSize = pageSize;
        Filters = filters is null ? new Dictionary<string, string>() : new Dictionary<string, string>(filters);
    }

    public string? Query { get; }

    public int PageNumber { get; }

    public int PageSize { get; }

    public IReadOnlyDictionary<string, string> Filters { get; }
}
