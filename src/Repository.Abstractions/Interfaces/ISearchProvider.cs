using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Abstractions.Interfaces;

public interface ISearchProvider
{
    Task<Result<SearchResponse>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
}
