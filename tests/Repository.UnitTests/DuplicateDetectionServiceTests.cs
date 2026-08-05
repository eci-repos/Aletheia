using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Application;
using Repository.UnitTests.UseCases;

namespace Repository.UnitTests;

public class DuplicateDetectionServiceTests
{
    [Fact]
    public async Task FindDuplicateAsync_returns_null_when_hash_is_empty()
    {
        var repository = new FakeMetadataRepository();
        var service = new DuplicateDetectionService(repository);

        var result = await service.FindDuplicateAsync("  ");

        Assert.Null(result);
        Assert.Null(repository.LastFindByContentHash);
    }

    [Fact]
    public async Task FindDuplicateAsync_returns_null_when_no_row_matches()
    {
        var repository = new FakeMetadataRepository
        {
            FindByContentHashResult = Result<FileMetadata?>.Success(null)
        };
        var service = new DuplicateDetectionService(repository);

        var result = await service.FindDuplicateAsync("abc123");

        Assert.Null(result);
        Assert.Equal("abc123", repository.LastFindByContentHash);
    }

    [Fact]
    public async Task FindDuplicateAsync_returns_existing_upload_when_hash_matches()
    {
        var descriptor = new FileDescriptor(Guid.NewGuid(), "report.pdf");
        var metadata = new FileMetadata(descriptor, "application/pdf", 10, DateTimeOffset.UtcNow, contentHash: "abc123");
        var repository = new FakeMetadataRepository
        {
            FindByContentHashResult = Result<FileMetadata?>.Success(metadata)
        };
        var service = new DuplicateDetectionService(repository);

        var result = await service.FindDuplicateAsync("abc123");

        Assert.NotNull(result);
        Assert.Equal(descriptor.FileId, result.FileId);
        Assert.Equal("report.pdf", result.FileName);
        Assert.Equal(metadata.SizeBytes, result.SizeBytes);
        Assert.Equal("abc123", repository.LastFindByContentHash);
    }

    [Fact]
    public async Task FindDuplicateAsync_returns_null_when_lookup_fails()
    {
        var repository = new FakeMetadataRepository
        {
            FindByContentHashResult = Result<FileMetadata?>.Failure("lookup failed")
        };
        var service = new DuplicateDetectionService(repository);

        var result = await service.FindDuplicateAsync("abc123");

        Assert.Null(result);
    }
}

