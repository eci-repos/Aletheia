using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Domain.UseCases;

public interface IVersioningUseCase
{
    Task<Result<FileDescriptor>> CreateVersionAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyCollection<FileDescriptor>>> ListVersionsAsync(
        FileDescriptor descriptor,
        CancellationToken cancellationToken = default);
}
