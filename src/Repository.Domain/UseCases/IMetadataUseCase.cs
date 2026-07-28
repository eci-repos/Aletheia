using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Domain.UseCases;

public interface IMetadataUseCase
{
    Task<Result<FileMetadata>> GetAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default);

    Task<Result<FileMetadata>> SaveAsync(FileMetadata metadata, CancellationToken cancellationToken = default);
}
