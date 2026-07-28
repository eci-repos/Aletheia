using System.Reflection;
using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Application;
using Aletheia.Repository.Application.UseCases;

namespace Repository.UnitTests.UseCases;

public class UploadUseCaseEdgeTests
{
    [Fact]
    public void Constructor_throws_when_storage_provider_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new UploadUseCase(null!, new FakeMetadataRepository()));
    }

    [Fact]
    public void Constructor_throws_when_metadata_repository_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new UploadUseCase(new FakeStorageProvider(), null!));
    }

    [Fact]
    public async Task UploadAsync_returns_failure_when_upload_result_has_null_value()
    {
        var storageProvider = new FakeStorageProvider
        {
            UploadResult = Result<UploadResponse>.Success(null!)
        };
        var metadataRepository = new FakeMetadataRepository();
        var useCase = new UploadUseCase(storageProvider, metadataRepository);
        var request = UseCaseTestData.CreateUploadRequest();

        var result = await useCase.UploadAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("Upload failed.", result.Error);
    }

    [Fact]
    public async Task UploadAsync_returns_failure_when_upload_error_is_missing()
    {
        var storageProvider = new FakeStorageProvider
        {
            UploadResult = ResultTestFactory.CreateFailureWithNullError<UploadResponse>()
        };
        var metadataRepository = new FakeMetadataRepository();
        var useCase = new UploadUseCase(storageProvider, metadataRepository);
        var request = UseCaseTestData.CreateUploadRequest();

        var result = await useCase.UploadAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("Upload failed.", result.Error);
    }

    [Fact]
    public async Task UploadAsync_returns_failure_when_metadata_error_is_missing()
    {
        var uploadedMetadata = UseCaseTestData.CreateMetadata();
        var storageProvider = new FakeStorageProvider
        {
            UploadResult = Result<UploadResponse>.Success(new UploadResponse(uploadedMetadata))
        };
        var metadataRepository = new FakeMetadataRepository
        {
            SaveResult = ResultTestFactory.CreateFailureWithNullError<FileMetadata>()
        };
        var useCase = new UploadUseCase(storageProvider, metadataRepository);
        var request = UseCaseTestData.CreateUploadRequest(uploadedMetadata.Descriptor);

        var result = await useCase.UploadAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata save failed.", result.Error);
    }

    [Fact]
    public async Task UploadAsync_returns_failure_when_metadata_result_has_null_value()
    {
        var uploadedMetadata = UseCaseTestData.CreateMetadata();
        var storageProvider = new FakeStorageProvider
        {
            UploadResult = Result<UploadResponse>.Success(new UploadResponse(uploadedMetadata))
        };
        var metadataRepository = new FakeMetadataRepository
        {
            SaveResult = Result<FileMetadata>.Success(null!)
        };
        var useCase = new UploadUseCase(storageProvider, metadataRepository);
        var request = UseCaseTestData.CreateUploadRequest(uploadedMetadata.Descriptor);

        var result = await useCase.UploadAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata save failed.", result.Error);
    }
}

public class DeleteUseCaseEdgeTests
{
    [Fact]
    public void Constructor_throws_when_storage_provider_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new DeleteUseCase(null!, new FakeMetadataRepository()));
    }

    [Fact]
    public void Constructor_throws_when_metadata_repository_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new DeleteUseCase(new FakeStorageProvider(), null!));
    }

    [Fact]
    public async Task DeleteAsync_returns_failure_when_storage_error_is_missing()
    {
        var storageProvider = new FakeStorageProvider
        {
            DeleteResult = ResultTestFactory.CreateFailureWithNullError()
        };
        var useCase = new DeleteUseCase(storageProvider, new FakeMetadataRepository());

        var result = await useCase.DeleteAsync(new DeleteRequest(UseCaseTestData.CreateDescriptor()));

        Assert.True(result.IsFailure);
        Assert.Equal("Delete failed.", result.Error);
    }

    [Fact]
    public async Task DeleteAsync_returns_failure_when_metadata_delete_fails()
    {
        var storageProvider = new FakeStorageProvider
        {
            DeleteResult = Result.Success()
        };
        var metadataRepository = new FakeMetadataRepository
        {
            DeleteResult = Result.Failure("metadata delete failed")
        };
        var useCase = new DeleteUseCase(storageProvider, metadataRepository);

        var result = await useCase.DeleteAsync(new DeleteRequest(UseCaseTestData.CreateDescriptor()));

        Assert.True(result.IsFailure);
        Assert.Equal("metadata delete failed", result.Error);
    }
}

public class DownloadUseCaseEdgeTests
{
    [Fact]
    public void Constructor_throws_when_storage_provider_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new DownloadUseCase(null!));
    }

    [Fact]
    public async Task DownloadAsync_returns_failure_when_download_error_is_missing()
    {
        var storageProvider = new FakeStorageProvider
        {
            DownloadResult = ResultTestFactory.CreateFailureWithNullError<DownloadResponse>()
        };
        var useCase = new DownloadUseCase(storageProvider);
        var request = UseCaseTestData.CreateDownloadRequest();

        var result = await useCase.DownloadAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("Download failed.", result.Error);
    }

    [Fact]
    public async Task DownloadAsync_returns_failure_when_download_result_has_null_value()
    {
        var storageProvider = new FakeStorageProvider
        {
            DownloadResult = Result<DownloadResponse>.Success(null!)
        };
        var useCase = new DownloadUseCase(storageProvider);
        var request = UseCaseTestData.CreateDownloadRequest();

        var result = await useCase.DownloadAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("Download failed.", result.Error);
    }
}

public class SearchUseCaseEdgeTests
{
    [Fact]
    public void Constructor_throws_when_search_provider_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new SearchUseCase(null!));
    }

    [Fact]
    public async Task SearchAsync_returns_failure_when_search_error_is_missing()
    {
        var searchProvider = new FakeSearchProvider
        {
            Result = ResultTestFactory.CreateFailureWithNullError<SearchResponse>()
        };
        var useCase = new SearchUseCase(searchProvider);
        var request = UseCaseTestData.CreateSearchRequest();

        var result = await useCase.SearchAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("Search failed.", result.Error);
    }

    [Fact]
    public async Task SearchAsync_returns_failure_when_search_result_has_null_value()
    {
        var searchProvider = new FakeSearchProvider
        {
            Result = Result<SearchResponse>.Success(null!)
        };
        var useCase = new SearchUseCase(searchProvider);
        var request = UseCaseTestData.CreateSearchRequest();

        var result = await useCase.SearchAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("Search failed.", result.Error);
    }
}

public class MetadataUseCaseEdgeTests
{
    [Fact]
    public void Constructor_throws_when_metadata_repository_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new MetadataUseCase(null!));
    }

    [Fact]
    public async Task GetAsync_returns_failure_when_error_is_missing()
    {
        var repository = new FakeMetadataRepository
        {
            GetResult = ResultTestFactory.CreateFailureWithNullError<FileMetadata>()
        };
        var useCase = new MetadataUseCase(repository);
        var descriptor = UseCaseTestData.CreateDescriptor();

        var result = await useCase.GetAsync(descriptor);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata retrieval failed.", result.Error);
    }

    [Fact]
    public async Task GetAsync_returns_failure_when_result_has_null_value()
    {
        var repository = new FakeMetadataRepository
        {
            GetResult = Result<FileMetadata>.Success(null!)
        };
        var useCase = new MetadataUseCase(repository);
        var descriptor = UseCaseTestData.CreateDescriptor();

        var result = await useCase.GetAsync(descriptor);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata retrieval failed.", result.Error);
    }

    [Fact]
    public async Task SaveAsync_throws_when_metadata_is_null()
    {
        var useCase = new MetadataUseCase(new FakeMetadataRepository());

        await Assert.ThrowsAsync<ArgumentNullException>(() => useCase.SaveAsync(null!));
    }

    [Fact]
    public async Task SaveAsync_returns_failure_when_error_is_missing()
    {
        var metadata = UseCaseTestData.CreateMetadata();
        var repository = new FakeMetadataRepository
        {
            SaveResult = ResultTestFactory.CreateFailureWithNullError<FileMetadata>()
        };
        var useCase = new MetadataUseCase(repository);

        var result = await useCase.SaveAsync(metadata);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata save failed.", result.Error);
    }

    [Fact]
    public async Task SaveAsync_returns_failure_when_result_has_null_value()
    {
        var metadata = UseCaseTestData.CreateMetadata();
        var repository = new FakeMetadataRepository
        {
            SaveResult = Result<FileMetadata>.Success(null!)
        };
        var useCase = new MetadataUseCase(repository);

        var result = await useCase.SaveAsync(metadata);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata save failed.", result.Error);
    }
}

public class VersioningUseCaseEdgeTests
{
    [Fact]
    public void Constructor_throws_when_versioning_service_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new VersioningUseCase(null!));
    }

    [Fact]
    public async Task CreateVersionAsync_returns_failure_when_error_is_missing()
    {
        var service = new FakeVersioningService
        {
            CreateResult = ResultTestFactory.CreateFailureWithNullError<FileDescriptor>()
        };
        var useCase = new VersioningUseCase(service);
        var descriptor = UseCaseTestData.CreateDescriptor();

        var result = await useCase.CreateVersionAsync(descriptor);

        Assert.True(result.IsFailure);
        Assert.Equal("Version creation failed.", result.Error);
    }

    [Fact]
    public async Task CreateVersionAsync_returns_failure_when_result_has_null_value()
    {
        var service = new FakeVersioningService
        {
            CreateResult = Result<FileDescriptor>.Success(null!)
        };
        var useCase = new VersioningUseCase(service);
        var descriptor = UseCaseTestData.CreateDescriptor();

        var result = await useCase.CreateVersionAsync(descriptor);

        Assert.True(result.IsFailure);
        Assert.Equal("Version creation failed.", result.Error);
    }

    [Fact]
    public async Task ListVersionsAsync_returns_failure_when_error_is_missing()
    {
        var service = new FakeVersioningService
        {
            ListResult = ResultTestFactory.CreateFailureWithNullError<IReadOnlyCollection<FileDescriptor>>()
        };
        var useCase = new VersioningUseCase(service);
        var descriptor = UseCaseTestData.CreateDescriptor();

        var result = await useCase.ListVersionsAsync(descriptor);

        Assert.True(result.IsFailure);
        Assert.Equal("Version listing failed.", result.Error);
    }

    [Fact]
    public async Task ListVersionsAsync_returns_failure_when_result_has_null_value()
    {
        var service = new FakeVersioningService
        {
            ListResult = Result<IReadOnlyCollection<FileDescriptor>>.Success(null!)
        };
        var useCase = new VersioningUseCase(service);
        var descriptor = UseCaseTestData.CreateDescriptor();

        var result = await useCase.ListVersionsAsync(descriptor);

        Assert.True(result.IsFailure);
        Assert.Equal("Version listing failed.", result.Error);
    }
}

public class RepositoryServiceConstructorTests
{
    [Fact]
    public void Constructor_throws_when_upload_use_case_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new RepositoryService(null!, new FakeDownloadUseCase(), new FakeSearchUseCase()));
    }

    [Fact]
    public void Constructor_throws_when_download_use_case_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new RepositoryService(new FakeUploadUseCase(), null!, new FakeSearchUseCase()));
    }

    [Fact]
    public void Constructor_throws_when_search_use_case_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new RepositoryService(new FakeUploadUseCase(), new FakeDownloadUseCase(), null!));
    }
}

internal static class ResultTestFactory
{
    public static Result CreateFailureWithNullError()
    {
        return (Result)Activator.CreateInstance(
            typeof(Result),
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            args: new object?[] { false, null },
            culture: null)!;
    }

    public static Result<T> CreateFailureWithNullError<T>()
    {
        return (Result<T>)Activator.CreateInstance(
            typeof(Result<T>),
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            args: new object?[] { false, default(T), null },
            culture: null)!;
    }
}
