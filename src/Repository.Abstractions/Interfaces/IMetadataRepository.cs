using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Abstractions.Interfaces;

public interface IMetadataRepository
{
    Task<Result<FileMetadata>> GetAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default);

    /// <summary>Resolves the current (unversioned) row for a file by id alone, or success(null) when not found. Used by the preview endpoint, which only knows the file id.</summary>
    Task<Result<FileMetadata?>> GetByFileIdAsync(Guid fileId, string? version = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<FileMetadata?>.Success(null));
    }

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

    /// <summary>Persists the canonical template name, knowledge themes, and template status for every row of the given file. No-op when not supported.</summary>
    Task<Result> SetTemplateAsync(
        Guid fileId,
        string? templateName,
        IReadOnlyList<string>? themes,
        string? templateStatus = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success());
    }

    /// <summary>Returns one row per file_metadata row with template/theme information for knowledge-theme counts. Empty when not supported.</summary>
    Task<Result<IReadOnlyList<FileThemeRow>>> ListThemeRowsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<IReadOnlyList<FileThemeRow>>.Success(new List<FileThemeRow>()));
    }

    /// <summary>Returns rows that are not Canonical (null or Uncategorized template status) for the admin uncategorized list and re-evaluation. Empty when not supported.</summary>
    Task<Result<IReadOnlyList<FileThemeRow>>> ListUncategorizedAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<IReadOnlyList<FileThemeRow>>.Success(new List<FileThemeRow>()));
    }
}