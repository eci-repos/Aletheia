using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Abstractions.Interfaces;

public interface IStorageProvider
{
    Task<Result<UploadResponse>> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default);

    Task<Result<DownloadResponse>> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(DeleteRequest request, CancellationToken cancellationToken = default);
}
