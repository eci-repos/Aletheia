using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Domain.UseCases;

namespace Aletheia.Repository.Application.UseCases;

public sealed class SearchUseCase : ISearchUseCase
{
    private const string SearchFailedMessage = "Search failed.";

    private readonly ISearchProvider _searchProvider;

    public SearchUseCase(ISearchProvider searchProvider)
    {
        _searchProvider = searchProvider ?? throw new ArgumentNullException(nameof(searchProvider));
    }

    public async Task<Result<SearchResponse>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var searchResult = await _searchProvider.SearchAsync(request, cancellationToken);
        if (searchResult.IsFailure)
        {
            return Result<SearchResponse>.Failure(searchResult.Error ?? SearchFailedMessage);
        }

        if (searchResult.Value is null)
        {
            return Result<SearchResponse>.Failure(SearchFailedMessage);
        }

        return searchResult;
    }
}
