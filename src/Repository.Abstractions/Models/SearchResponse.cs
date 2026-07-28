using Aletheia.Foundation.Shared;

namespace Aletheia.Repository.Abstractions.Models;

public sealed class SearchResponse
{
    public SearchResponse(PagedResult<FileMetadata> results)
    {
        Results = results ?? throw new ArgumentNullException(nameof(results));
    }

    public PagedResult<FileMetadata> Results { get; }
}
