using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Domain.UseCases;

namespace Aletheia.Repository.Application.UseCases;

public sealed class DownloadUseCase : IDownloadUseCase
{
    private const string DownloadFailedMessage = "Download failed.";

    private readonly IStorageProvider _storageProvider;

    public DownloadUseCase(IStorageProvider storageProvider)
    {
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
    }

    public async Task<Result<DownloadResponse>> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var downloadResult = await _storageProvider.DownloadAsync(request, cancellationToken);
        if (downloadResult.IsFailure)
        {
            return Result<DownloadResponse>.Failure(downloadResult.Error ?? DownloadFailedMessage);
        }

        if (downloadResult.Value is null)
        {
            return Result<DownloadResponse>.Failure(DownloadFailedMessage);
        }

        return downloadResult;
    }
}
