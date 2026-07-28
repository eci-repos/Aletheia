using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Domain.UseCases;

namespace Aletheia.Repository.Application.UseCases;

public sealed class VersioningUseCase : IVersioningUseCase
{
    private const string VersionCreateFailedMessage = "Version creation failed.";
    private const string VersionListFailedMessage = "Version listing failed.";

    private readonly IVersioningService _versioningService;

    public VersioningUseCase(IVersioningService versioningService)
    {
        _versioningService = versioningService ?? throw new ArgumentNullException(nameof(versioningService));
    }

    public async Task<Result<FileDescriptor>> CreateVersionAsync(
        FileDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        var versionResult = await _versioningService.CreateVersionAsync(descriptor, cancellationToken);
        if (versionResult.IsFailure)
        {
            return Result<FileDescriptor>.Failure(versionResult.Error ?? VersionCreateFailedMessage);
        }

        if (versionResult.Value is null)
        {
            return Result<FileDescriptor>.Failure(VersionCreateFailedMessage);
        }

        return versionResult;
    }

    public async Task<Result<IReadOnlyCollection<FileDescriptor>>> ListVersionsAsync(
        FileDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        var versionsResult = await _versioningService.ListVersionsAsync(descriptor, cancellationToken);
        if (versionsResult.IsFailure)
        {
            return Result<IReadOnlyCollection<FileDescriptor>>.Failure(
                versionsResult.Error ?? VersionListFailedMessage);
        }

        if (versionsResult.Value is null)
        {
            return Result<IReadOnlyCollection<FileDescriptor>>.Failure(VersionListFailedMessage);
        }

        return versionsResult;
    }
}
