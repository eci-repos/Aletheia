using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Domain.UseCases;

public interface IUploadUseCase
{
    Task<Result<UploadResponse>> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default);
}
