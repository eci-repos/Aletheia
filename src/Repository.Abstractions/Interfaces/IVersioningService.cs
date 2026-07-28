using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Abstractions.Interfaces;

public interface IVersioningService
{
    Task<Result<FileDescriptor>> CreateVersionAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyCollection<FileDescriptor>>> ListVersionsAsync(
        FileDescriptor descriptor,
        CancellationToken cancellationToken = default);
}
