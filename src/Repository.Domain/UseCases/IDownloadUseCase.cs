using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Domain.UseCases;

public interface IDownloadUseCase
{
    Task<Result<DownloadResponse>> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default);
}
