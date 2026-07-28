using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Domain.UseCases;

namespace Aletheia.Repository.Application.UseCases;

public sealed class DeleteUseCase : IDeleteUseCase
{
    private const string DeleteFailedMessage = "Delete failed.";
    private const string MetadataDeleteFailedMessage = "Metadata delete failed.";

    private readonly IStorageProvider _storageProvider;
    private readonly IMetadataRepository _metadataRepository;

    public DeleteUseCase(IStorageProvider storageProvider, IMetadataRepository metadataRepository)
    {
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
    }

    public async Task<Result> DeleteAsync(DeleteRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var storageResult = await _storageProvider.DeleteAsync(request, cancellationToken).ConfigureAwait(false);
        if (storageResult.IsFailure)
        {
            return Result.Failure(storageResult.Error ?? DeleteFailedMessage);
        }

        var metadataResult = await _metadataRepository.DeleteAsync(request.Descriptor, cancellationToken).ConfigureAwait(false);
        if (metadataResult.IsFailure)
        {
            return Result.Failure(metadataResult.Error ?? MetadataDeleteFailedMessage);
        }

        return Result.Success();
    }
}
