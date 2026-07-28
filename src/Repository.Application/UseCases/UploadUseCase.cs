using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Domain.UseCases;

namespace Aletheia.Repository.Application.UseCases;

public sealed class UploadUseCase : IUploadUseCase
{
    private const string UploadFailedMessage = "Upload failed.";
    private const string MetadataSaveFailedMessage = "Metadata save failed.";

    private readonly IStorageProvider _storageProvider;
    private readonly IMetadataRepository _metadataRepository;

    public UploadUseCase(IStorageProvider storageProvider, IMetadataRepository metadataRepository)
    {
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
    }

    public async Task<Result<UploadResponse>> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var uploadResult = await _storageProvider.UploadAsync(request, cancellationToken);
        if (uploadResult.IsFailure)
        {
            return Result<UploadResponse>.Failure(uploadResult.Error ?? UploadFailedMessage);
        }

        if (uploadResult.Value is null)
        {
            return Result<UploadResponse>.Failure(UploadFailedMessage);
        }

        var metadataResult = await _metadataRepository.SaveAsync(uploadResult.Value.Metadata, cancellationToken);
        if (metadataResult.IsFailure)
        {
            return Result<UploadResponse>.Failure(metadataResult.Error ?? MetadataSaveFailedMessage);
        }

        if (metadataResult.Value is null)
        {
            return Result<UploadResponse>.Failure(MetadataSaveFailedMessage);
        }

        return Result<UploadResponse>.Success(new UploadResponse(metadataResult.Value));
    }
}
