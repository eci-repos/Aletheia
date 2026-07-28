using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Domain.UseCases;

public interface ISearchUseCase
{
    Task<Result<SearchResponse>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
}
