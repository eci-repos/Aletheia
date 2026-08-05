using Aletheia.Repository.Abstractions.Interfaces;

namespace Aletheia.Repository.Application;

/// <summary>Identifies whether the given content hash already exists in the repository (tenant-scoped lookup).</summary>
public interface IDuplicateDetectionService
{
    /// <summary>Returns the most recent matching upload, or null when the content is not already stored.</summary>
    Task<DuplicateUpload?> FindDuplicateAsync(string contentHash, CancellationToken cancellationToken = default);
}

public sealed record DuplicateUpload(
    Guid FileId,
    string FileName,
    DateTimeOffset UploadedAt,
    string? Version,
    long SizeBytes);

public sealed class DuplicateDetectionService : IDuplicateDetectionService
{
    private readonly IMetadataRepository _metadataRepository;

    public DuplicateDetectionService(IMetadataRepository metadataRepository)
    {
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
    }

    public async Task<DuplicateUpload?> FindDuplicateAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return null;
        }

        var result = await _metadataRepository.FindByContentHashAsync(contentHash, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure || result.Value is null)
        {
            return null;
        }

        var metadata = result.Value;
        return new DuplicateUpload(
            metadata.Descriptor.FileId,
            metadata.Descriptor.FileName,
            metadata.UploadedAt,
            metadata.Descriptor.Version,
            metadata.SizeBytes);
    }
}
