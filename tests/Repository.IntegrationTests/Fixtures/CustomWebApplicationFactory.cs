using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Infrastructure.PostgreSQL.Security;
using Aletheia.Repository.Domain.UseCases;
using Aletheia.Security.Authentication;
using Aletheia.Security.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Repository.IntegrationTests.Fixtures;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Authentication:Jwt:Secret", "test-secret-that-is-at-least-32-characters-long-for-jwt-signing");

        builder.ConfigureServices(services =>
        {
            // Replace all infrastructure with fakes for API tests
            ReplaceService<IStorageProvider, FakeStorageProvider>(services);
            ReplaceService<IMetadataRepository, FakeMetadataRepository>(services);
            ReplaceService<ISearchProvider, FakeSearchProvider>(services);
            ReplaceService<IVersioningService, FakeVersioningService>(services);
            services.RemoveAll<IUserStore>();
            services.RemoveAll<IRefreshTokenStore>();
            // Remove PostgreSQL wiki schema initializer (uses real DB)
// Removed removal of PostgreSqlWikiSchemaInitializer as the type no longer exists in the codebase.

            services.AddSingleton<IUserStore, InMemoryUserStore>();
            services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
        });
    }

    private static void ReplaceService<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(TService));
        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }

        services.AddSingleton<TService, TImplementation>();
    }
}

internal sealed class FakeStorageProvider : IStorageProvider
{
    public Task<Result<UploadResponse>> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default)
    {
        var metadata = new FileMetadata(
            request.Descriptor,
            request.ContentType,
            request.SizeBytes,
            DateTimeOffset.UtcNow,
            request.Tags);
        return Task.FromResult(Result<UploadResponse>.Success(new UploadResponse(metadata)));
    }

    public Task<Result<DownloadResponse>> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        var metadata = new FileMetadata(
            request.Descriptor,
            "application/octet-stream",
            0,
            DateTimeOffset.UtcNow);
        return Task.FromResult(Result<DownloadResponse>.Success(new DownloadResponse(metadata, new MemoryStream())));
    }

    public Task<Result> DeleteAsync(DeleteRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success());
    }
}

internal sealed class FakeMetadataRepository : IMetadataRepository
{
    public Task<Result<FileMetadata>> GetAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<FileMetadata>.Failure("not found"));
    }

    public Task<Result<FileMetadata>> SaveAsync(FileMetadata metadata, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<FileMetadata>.Success(metadata));
    }

    public Task<Result<PagedResult<FileMetadata>>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        var result = new PagedResult<FileMetadata>(Array.Empty<FileMetadata>(), request.PageNumber, request.PageSize, 0);
        return Task.FromResult(Result<PagedResult<FileMetadata>>.Success(result));
    }

    public Task<Result> DeleteAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success());
    }
}

internal sealed class FakeSearchProvider : ISearchProvider
{
    public Task<Result<SearchResponse>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        var result = new PagedResult<FileMetadata>(Array.Empty<FileMetadata>(), request.PageNumber, request.PageSize, 0);
        return Task.FromResult(Result<SearchResponse>.Success(new SearchResponse(result)));
    }
}

internal sealed class FakeVersioningService : IVersioningService
{
    public Task<Result<FileDescriptor>> CreateVersionAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<FileDescriptor>.Success(descriptor));
    }

    public Task<Result<IReadOnlyCollection<FileDescriptor>>> ListVersionsAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<IReadOnlyCollection<FileDescriptor>>.Success(new[] { descriptor }));
    }
}
