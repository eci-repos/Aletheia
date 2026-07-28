using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Infrastructure.PostgreSQL.Search;

public sealed class PostgreSqlSearchProvider : ISearchProvider
{
    private readonly IMetadataRepository _metadataRepository;

    public PostgreSqlSearchProvider(IMetadataRepository metadataRepository)
    {
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
    }

    public async Task<Result<SearchResponse>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var result = await _metadataRepository.SearchAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<SearchResponse>.Failure(result.Error ?? "Search failed.");
        }

        if (result.Value is null)
        {
            return Result<SearchResponse>.Failure("Search returned no results.");
        }

        return Result<SearchResponse>.Success(new SearchResponse(result.Value));
    }
}
