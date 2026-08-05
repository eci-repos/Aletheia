using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Abstractions.Interfaces;

public interface IMetadataRepository
{
    Task<Result<FileMetadata>> GetAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default);

    Task<Result<FileMetadata>> SaveAsync(FileMetadata metadata, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<FileMetadata>>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default);

    /// <summary>Returns the most recently uploaded row with the given content hash, or success(null) when none exists.</summary>
    Task<Result<FileMetadata?>> FindByContentHashAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<FileMetadata?>.Success(null));
    }

    /// <summary>Returns every row whose content hash is shared by more than one row (candidate duplicates), newest first.</summary>
    Task<Result<IReadOnlyList<FileMetadata>>> ListContentHashDuplicatesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<IReadOnlyList<FileMetadata>>.Success(new List<FileMetadata>()));
    }
}
