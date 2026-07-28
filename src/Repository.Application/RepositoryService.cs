using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Domain.UseCases;

namespace Aletheia.Repository.Application;

public sealed class RepositoryService : IRepositoryService
{
    private readonly IUploadUseCase _uploadUseCase;
    private readonly IDownloadUseCase _downloadUseCase;
    private readonly ISearchUseCase _searchUseCase;

    public RepositoryService(
        IUploadUseCase uploadUseCase,
        IDownloadUseCase downloadUseCase,
        ISearchUseCase searchUseCase)
    {
        _uploadUseCase = uploadUseCase ?? throw new ArgumentNullException(nameof(uploadUseCase));
        _downloadUseCase = downloadUseCase ?? throw new ArgumentNullException(nameof(downloadUseCase));
        _searchUseCase = searchUseCase ?? throw new ArgumentNullException(nameof(searchUseCase));
    }

    public Task<Result<UploadResponse>> UploadAsync(
        UploadRequest request,
        CancellationToken cancellationToken = default)
    {
        return _uploadUseCase.UploadAsync(request, cancellationToken);
    }

    public Task<Result<DownloadResponse>> DownloadAsync(
        DownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        return _downloadUseCase.DownloadAsync(request, cancellationToken);
    }

    public Task<Result<SearchResponse>> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        return _searchUseCase.SearchAsync(request, cancellationToken);
    }
}
