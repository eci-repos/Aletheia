using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Domain.UseCases;

namespace Aletheia.Repository.Application.UseCases;

public sealed class MetadataUseCase : IMetadataUseCase
{
    private const string MetadataGetFailedMessage = "Metadata retrieval failed.";
    private const string MetadataSaveFailedMessage = "Metadata save failed.";

    private readonly IMetadataRepository _metadataRepository;

    public MetadataUseCase(IMetadataRepository metadataRepository)
    {
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
    }

    public async Task<Result<FileMetadata>> GetAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        var metadataResult = await _metadataRepository.GetAsync(descriptor, cancellationToken);
        if (metadataResult.IsFailure)
        {
            return Result<FileMetadata>.Failure(metadataResult.Error ?? MetadataGetFailedMessage);
        }

        if (metadataResult.Value is null)
        {
            return Result<FileMetadata>.Failure(MetadataGetFailedMessage);
        }

        return metadataResult;
    }

    public async Task<Result<FileMetadata>> SaveAsync(FileMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        var metadataResult = await _metadataRepository.SaveAsync(metadata, cancellationToken);
        if (metadataResult.IsFailure)
        {
            return Result<FileMetadata>.Failure(metadataResult.Error ?? MetadataSaveFailedMessage);
        }

        if (metadataResult.Value is null)
        {
            return Result<FileMetadata>.Failure(MetadataSaveFailedMessage);
        }

        return metadataResult;
    }
}
