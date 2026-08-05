using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Application;
using Aletheia.Repository.Application.UseCases;
using Aletheia.Repository.Domain.UseCases;

namespace Repository.UnitTests.UseCases;

public class UploadUseCaseTests
{
    [Fact]
    public async Task UploadAsync_throws_when_request_is_null()
    {
        var useCase = new UploadUseCase(new FakeStorageProvider(), new FakeMetadataRepository());

        await Assert.ThrowsAsync<ArgumentNullException>(() => useCase.UploadAsync(null!));
    }

    [Fact]
    public async Task UploadAsync_returns_failure_when_storage_provider_fails()
    {
        var storageProvider = new FakeStorageProvider
        {
            UploadResult = Result<UploadResponse>.Failure("storage unavailable")
        };
        var metadataRepository = new FakeMetadataRepository();
        var useCase = new UploadUseCase(storageProvider, metadataRepository);
        var request = UseCaseTestData.CreateUploadRequest();

        var result = await useCase.UploadAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("storage unavailable", result.Error);
        Assert.Null(metadataRepository.LastSavedMetadata);
    }

    [Fact]
    public async Task UploadAsync_returns_failure_when_metadata_save_fails()
    {
        var descriptor = UseCaseTestData.CreateDescriptor();
        var uploadedMetadata = UseCaseTestData.CreateMetadata(descriptor);
        var storageProvider = new FakeStorageProvider
        {
            UploadResult = Result<UploadResponse>.Success(new UploadResponse(uploadedMetadata))
        };
        var metadataRepository = new FakeMetadataRepository
        {
            SaveResult = Result<FileMetadata>.Failure("metadata failure")
        };
        var useCase = new UploadUseCase(storageProvider, metadataRepository);
        var request = UseCaseTestData.CreateUploadRequest(descriptor);

        var result = await useCase.UploadAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("metadata failure", result.Error);
        Assert.Equal(uploadedMetadata, metadataRepository.LastSavedMetadata);
    }

    [Fact]
    public async Task UploadAsync_saves_metadata_when_upload_succeeds()
    {
        var descriptor = UseCaseTestData.CreateDescriptor();
        var uploadedMetadata = UseCaseTestData.CreateMetadata(descriptor);
        var storageProvider = new FakeStorageProvider
        {
            UploadResult = Result<UploadResponse>.Success(new UploadResponse(uploadedMetadata))
        };
        var metadataRepository = new FakeMetadataRepository
        {
            SaveResult = Result<FileMetadata>.Success(uploadedMetadata)
        };
        var useCase = new UploadUseCase(storageProvider, metadataRepository);
        var request = UseCaseTestData.CreateUploadRequest(descriptor);

        var result = await useCase.UploadAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(uploadedMetadata, metadataRepository.LastSavedMetadata);
        Assert.Equal(uploadedMetadata, result.Value?.Metadata);
    }
}

public class DownloadUseCaseTests
{
    [Fact]
    public async Task DownloadAsync_throws_when_request_is_null()
    {
        var useCase = new DownloadUseCase(new FakeStorageProvider());

        await Assert.ThrowsAsync<ArgumentNullException>(() => useCase.DownloadAsync(null!));
    }

    [Fact]
    public async Task DownloadAsync_returns_failure_when_storage_provider_fails()
    {
        var storageProvider = new FakeStorageProvider
        {
            DownloadResult = Result<DownloadResponse>.Failure("missing blob")
        };
        var useCase = new DownloadUseCase(storageProvider);
        var request = UseCaseTestData.CreateDownloadRequest();

        var result = await useCase.DownloadAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("missing blob", result.Error);
    }

    [Fact]
    public async Task DownloadAsync_returns_response_when_storage_provider_succeeds()
    {
        var metadata = UseCaseTestData.CreateMetadata();
        var response = new DownloadResponse(metadata, new MemoryStream(new byte[] { 1, 2, 3 }));
        var storageProvider = new FakeStorageProvider
        {
            DownloadResult = Result<DownloadResponse>.Success(response)
        };
        var useCase = new DownloadUseCase(storageProvider);
        var request = UseCaseTestData.CreateDownloadRequest(metadata.Descriptor);

        var result = await useCase.DownloadAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(response, result.Value);
    }
}

public class SearchUseCaseTests
{
    [Fact]
    public async Task SearchAsync_throws_when_request_is_null()
    {
        var useCase = new SearchUseCase(new FakeSearchProvider());

        await Assert.ThrowsAsync<ArgumentNullException>(() => useCase.SearchAsync(null!));
    }

    [Fact]
    public async Task SearchAsync_returns_failure_when_provider_fails()
    {
        var searchProvider = new FakeSearchProvider
        {
            Result = Result<SearchResponse>.Failure("search offline")
        };
        var useCase = new SearchUseCase(searchProvider);
        var request = UseCaseTestData.CreateSearchRequest();

        var result = await useCase.SearchAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("search offline", result.Error);
    }

    [Fact]
    public async Task SearchAsync_returns_response_when_provider_succeeds()
    {
        var metadata = UseCaseTestData.CreateMetadata();
        var results = new PagedResult<FileMetadata>(new[] { metadata }, 1, 10, 1);
        var response = new SearchResponse(results);
        var searchProvider = new FakeSearchProvider
        {
            Result = Result<SearchResponse>.Success(response)
        };
        var useCase = new SearchUseCase(searchProvider);
        var request = UseCaseTestData.CreateSearchRequest();

        var result = await useCase.SearchAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(response, result.Value);
    }
}

public class DeleteUseCaseTests
{
    [Fact]
    public async Task DeleteAsync_throws_when_request_is_null()
    {
        var useCase = new DeleteUseCase(new FakeStorageProvider(), new FakeMetadataRepository());

        await Assert.ThrowsAsync<ArgumentNullException>(() => useCase.DeleteAsync(null!));
    }

    [Fact]
    public async Task DeleteAsync_returns_failure_when_storage_delete_fails()
    {
        var storageProvider = new FakeStorageProvider
        {
            DeleteResult = Result.Failure("storage delete failed")
        };
        var metadataRepository = new FakeMetadataRepository();
        var useCase = new DeleteUseCase(storageProvider, metadataRepository);
        var request = new DeleteRequest(UseCaseTestData.CreateDescriptor());

        var result = await useCase.DeleteAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("storage delete failed", result.Error);
        Assert.Null(metadataRepository.LastDeletedDescriptor);
    }

    [Fact]
    public async Task DeleteAsync_deletes_metadata_when_storage_delete_succeeds()
    {
        var descriptor = UseCaseTestData.CreateDescriptor();
        var storageProvider = new FakeStorageProvider
        {
            DeleteResult = Result.Success()
        };
        var metadataRepository = new FakeMetadataRepository
        {
            DeleteResult = Result.Success()
        };
        var useCase = new DeleteUseCase(storageProvider, metadataRepository);
        var request = new DeleteRequest(descriptor);

        var result = await useCase.DeleteAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(request, storageProvider.LastDeleteRequest);
        Assert.Equal(descriptor, metadataRepository.LastDeletedDescriptor);
    }
}

public class MetadataUseCaseTests
{
    [Fact]
    public async Task GetAsync_throws_when_descriptor_is_null()
    {
        var useCase = new MetadataUseCase(new FakeMetadataRepository());

        await Assert.ThrowsAsync<ArgumentNullException>(() => useCase.GetAsync(null!));
    }

    [Fact]
    public async Task GetAsync_returns_failure_when_repository_fails()
    {
        var repository = new FakeMetadataRepository
        {
            GetResult = Result<FileMetadata>.Failure("missing metadata")
        };
        var useCase = new MetadataUseCase(repository);
        var descriptor = UseCaseTestData.CreateDescriptor();

        var result = await useCase.GetAsync(descriptor);

        Assert.True(result.IsFailure);
        Assert.Equal("missing metadata", result.Error);
        Assert.Equal(descriptor, repository.LastGetDescriptor);
    }

    [Fact]
    public async Task GetAsync_returns_metadata_when_repository_succeeds()
    {
        var descriptor = UseCaseTestData.CreateDescriptor();
        var metadata = UseCaseTestData.CreateMetadata(descriptor);
        var repository = new FakeMetadataRepository
        {
            GetResult = Result<FileMetadata>.Success(metadata)
        };
        var useCase = new MetadataUseCase(repository);

        var result = await useCase.GetAsync(descriptor);

        Assert.True(result.IsSuccess);
        Assert.Equal(metadata, result.Value);
        Assert.Equal(descriptor, repository.LastGetDescriptor);
    }

    [Fact]
    public async Task SaveAsync_returns_metadata_when_repository_succeeds()
    {
        var metadata = UseCaseTestData.CreateMetadata();
        var repository = new FakeMetadataRepository
        {
            SaveResult = Result<FileMetadata>.Success(metadata)
        };
        var useCase = new MetadataUseCase(repository);

        var result = await useCase.SaveAsync(metadata);

        Assert.True(result.IsSuccess);
        Assert.Equal(metadata, result.Value);
    }
}

public class VersioningUseCaseTests
{
    [Fact]
    public async Task CreateVersionAsync_throws_when_descriptor_is_null()
    {
        var useCase = new VersioningUseCase(new FakeVersioningService());

        await Assert.ThrowsAsync<ArgumentNullException>(() => useCase.CreateVersionAsync(null!));
    }

    [Fact]
    public async Task CreateVersionAsync_returns_failure_when_service_fails()
    {
        var service = new FakeVersioningService
        {
            CreateResult = Result<FileDescriptor>.Failure("version error")
        };
        var useCase = new VersioningUseCase(service);
        var descriptor = UseCaseTestData.CreateDescriptor();

        var result = await useCase.CreateVersionAsync(descriptor);

        Assert.True(result.IsFailure);
        Assert.Equal("version error", result.Error);
        Assert.Equal(descriptor, service.LastCreateDescriptor);
    }

    [Fact]
    public async Task CreateVersionAsync_returns_descriptor_when_service_succeeds()
    {
        var descriptor = UseCaseTestData.CreateDescriptor();
        var service = new FakeVersioningService
        {
            CreateResult = Result<FileDescriptor>.Success(descriptor)
        };
        var useCase = new VersioningUseCase(service);

        var result = await useCase.CreateVersionAsync(descriptor);

        Assert.True(result.IsSuccess);
        Assert.Equal(descriptor, result.Value);
        Assert.Equal(descriptor, service.LastCreateDescriptor);
    }

    [Fact]
    public async Task ListVersionsAsync_throws_when_descriptor_is_null()
    {
        var useCase = new VersioningUseCase(new FakeVersioningService());

        await Assert.ThrowsAsync<ArgumentNullException>(() => useCase.ListVersionsAsync(null!));
    }

    [Fact]
    public async Task ListVersionsAsync_returns_versions_when_service_succeeds()
    {
        var descriptor = UseCaseTestData.CreateDescriptor();
        var versions = new[] { descriptor };
        var service = new FakeVersioningService
        {
            ListResult = Result<IReadOnlyCollection<FileDescriptor>>.Success(versions)
        };
        var useCase = new VersioningUseCase(service);

        var result = await useCase.ListVersionsAsync(descriptor);

        Assert.True(result.IsSuccess);
        Assert.Equal(versions, result.Value);
    }
}

public class RepositoryServiceTests
{
    [Fact]
    public async Task UploadAsync_delegates_to_upload_use_case()
    {
        var uploadUseCase = new FakeUploadUseCase
        {
            Result = Result<UploadResponse>.Failure("upload error")
        };
        var service = new RepositoryService(uploadUseCase, new FakeDownloadUseCase(), new FakeSearchUseCase());
        var request = UseCaseTestData.CreateUploadRequest();

        var result = await service.UploadAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("upload error", result.Error);
        Assert.Equal(request, uploadUseCase.LastRequest);
    }

    [Fact]
    public async Task DownloadAsync_delegates_to_download_use_case()
    {
        var metadata = UseCaseTestData.CreateMetadata();
        var response = new DownloadResponse(metadata, new MemoryStream(new byte[] { 1 }));
        var downloadUseCase = new FakeDownloadUseCase
        {
            Result = Result<DownloadResponse>.Success(response)
        };
        var service = new RepositoryService(new FakeUploadUseCase(), downloadUseCase, new FakeSearchUseCase());
        var request = UseCaseTestData.CreateDownloadRequest(metadata.Descriptor);

        var result = await service.DownloadAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(response, result.Value);
        Assert.Equal(request, downloadUseCase.LastRequest);
    }

    [Fact]
    public async Task SearchAsync_delegates_to_search_use_case()
    {
        var response = new SearchResponse(new PagedResult<FileMetadata>(Array.Empty<FileMetadata>(), 1, 10, 0));
        var searchUseCase = new FakeSearchUseCase
        {
            Result = Result<SearchResponse>.Success(response)
        };
        var service = new RepositoryService(new FakeUploadUseCase(), new FakeDownloadUseCase(), searchUseCase);
        var request = UseCaseTestData.CreateSearchRequest();

        var result = await service.SearchAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(response, result.Value);
        Assert.Equal(request, searchUseCase.LastRequest);
    }
}

internal static class UseCaseTestData
{
    public static FileDescriptor CreateDescriptor()
    {
        return new FileDescriptor(Guid.NewGuid(), "report.pdf", "v1");
    }

    public static FileMetadata CreateMetadata(FileDescriptor? descriptor = null)
    {
        var resolvedDescriptor = descriptor ?? CreateDescriptor();
        return new FileMetadata(resolvedDescriptor, "application/pdf", 3, DateTimeOffset.UtcNow);
    }

    public static UploadRequest CreateUploadRequest(FileDescriptor? descriptor = null)
    {
        var resolvedDescriptor = descriptor ?? CreateDescriptor();
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        return new UploadRequest(resolvedDescriptor, stream, "application/pdf", 3);
    }

    public static DownloadRequest CreateDownloadRequest(FileDescriptor? descriptor = null)
    {
        var resolvedDescriptor = descriptor ?? CreateDescriptor();
        return new DownloadRequest(resolvedDescriptor);
    }

    public static SearchRequest CreateSearchRequest()
    {
        return new SearchRequest("query", 1, 10);
    }
}

internal sealed class FakeStorageProvider : IStorageProvider
{
    public Result<UploadResponse> UploadResult { get; set; } = Result<UploadResponse>.Failure("upload not configured");

    public Result<DownloadResponse> DownloadResult { get; set; } = Result<DownloadResponse>.Failure("download not configured");

    public Result DeleteResult { get; set; } = Result.Failure("delete not configured");

    public UploadRequest? LastUploadRequest { get; private set; }

    public DownloadRequest? LastDownloadRequest { get; private set; }

    public DeleteRequest? LastDeleteRequest { get; private set; }

    public Task<Result<UploadResponse>> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default)
    {
        LastUploadRequest = request;
        return Task.FromResult(UploadResult);
    }

    public Task<Result<DownloadResponse>> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        LastDownloadRequest = request;
        return Task.FromResult(DownloadResult);
    }

    public Task<Result> DeleteAsync(DeleteRequest request, CancellationToken cancellationToken = default)
    {
        LastDeleteRequest = request;
        return Task.FromResult(DeleteResult);
    }
}

internal sealed class FakeMetadataRepository : IMetadataRepository
{
    public Result<FileMetadata> GetResult { get; set; } = Result<FileMetadata>.Failure("get not configured");

    public Result<FileMetadata> SaveResult { get; set; } = Result<FileMetadata>.Failure("save not configured");

    public Result<PagedResult<FileMetadata>> SearchResult { get; set; } = Result<PagedResult<FileMetadata>>.Failure("search not configured");

    public Result DeleteResult { get; set; } = Result.Failure("delete not configured");

    public FileDescriptor? LastGetDescriptor { get; private set; }

    public FileMetadata? LastSavedMetadata { get; private set; }

    public SearchRequest? LastSearchRequest { get; private set; }

    public FileDescriptor? LastDeletedDescriptor { get; private set; }
    public Result<FileMetadata?> FindByContentHashResult { get; set; } = Result<FileMetadata?>.Success(null);

    public Result<IReadOnlyList<FileMetadata>> ListContentHashDuplicatesResult { get; set; }
        = Result<IReadOnlyList<FileMetadata>>.Success(new List<FileMetadata>());

    public string? LastFindByContentHash { get; private set; }

    public Task<Result<FileMetadata>> GetAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        LastGetDescriptor = descriptor;
        return Task.FromResult(GetResult);
    }

    public Task<Result<FileMetadata>> SaveAsync(FileMetadata metadata, CancellationToken cancellationToken = default)
    {
        LastSavedMetadata = metadata;
        return Task.FromResult(SaveResult);
    }

    public Task<Result<PagedResult<FileMetadata>>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        LastSearchRequest = request;
        return Task.FromResult(SearchResult);
    }

    public Task<Result> DeleteAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        LastDeletedDescriptor = descriptor;
        return Task.FromResult(DeleteResult);
    }

    public Task<Result<FileMetadata?>> FindByContentHashAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        LastFindByContentHash = contentHash;
        return Task.FromResult(FindByContentHashResult);
    }

    public Task<Result<IReadOnlyList<FileMetadata>>> ListContentHashDuplicatesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ListContentHashDuplicatesResult);
    }
}

internal sealed class FakeSearchProvider : ISearchProvider
{
    public Result<SearchResponse> Result { get; set; } = Result<SearchResponse>.Failure("search not configured");

    public SearchRequest? LastRequest { get; private set; }

    public Task<Result<SearchResponse>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(Result);
    }
}

internal sealed class FakeVersioningService : IVersioningService
{
    public Result<FileDescriptor> CreateResult { get; set; } = Result<FileDescriptor>.Failure("create not configured");

    public Result<IReadOnlyCollection<FileDescriptor>> ListResult { get; set; }
        = Result<IReadOnlyCollection<FileDescriptor>>.Failure("list not configured");

    public FileDescriptor? LastCreateDescriptor { get; private set; }

    public FileDescriptor? LastListDescriptor { get; private set; }

    public Task<Result<FileDescriptor>> CreateVersionAsync(
        FileDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        LastCreateDescriptor = descriptor;
        return Task.FromResult(CreateResult);
    }

    public Task<Result<IReadOnlyCollection<FileDescriptor>>> ListVersionsAsync(
        FileDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        LastListDescriptor = descriptor;
        return Task.FromResult(ListResult);
    }
}

internal sealed class FakeUploadUseCase : IUploadUseCase
{
    public Result<UploadResponse> Result { get; set; } = Result<UploadResponse>.Failure("upload not configured");

    public UploadRequest? LastRequest { get; private set; }

    public Task<Result<UploadResponse>> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(Result);
    }
}

internal sealed class FakeDownloadUseCase : IDownloadUseCase
{
    public Result<DownloadResponse> Result { get; set; } = Result<DownloadResponse>.Failure("download not configured");

    public DownloadRequest? LastRequest { get; private set; }

    public Task<Result<DownloadResponse>> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(Result);
    }
}

internal sealed class FakeSearchUseCase : ISearchUseCase
{
    public Result<SearchResponse> Result { get; set; } = Result<SearchResponse>.Failure("search not configured");

    public SearchRequest? LastRequest { get; private set; }

    public Task<Result<SearchResponse>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(Result);
    }
}


