using System.Security.Cryptography;
using System.Text.Json;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.API.Controllers;
using Aletheia.Repository.API.Services;
using Aletheia.Repository.Application;
using Aletheia.Repository.Domain.UseCases;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Repository.UnitTests.Controllers;

public class FilesControllerTests
{
    private static readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "aletheia-ingestion");

    private static readonly byte[] FileBytes = { 1, 2, 3, 4, 5 };
    private static readonly string FileHash = Convert.ToHexString(SHA256.HashData(FileBytes)).ToLowerInvariant();

    [Fact]
    public async Task Upload_returns_conflict_when_exact_duplicate_exists()
    {
        var duplicate = new DuplicateUpload(Guid.NewGuid(), "existing.pdf", DateTimeOffset.UtcNow, null, FileBytes.Length);
        var mocks = CreateMocks();
        mocks.DuplicateDetection
            .Setup(x => x.FindDuplicateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(duplicate);

        var controller = CreateController(mocks);
        try
        {
            var result = await controller.Upload(
                Guid.NewGuid(),
                "new.pdf",
                "application/pdf",
                FileBytes.Length,
                null,
                CreateFormFile(),
                CancellationToken.None);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            var root = ParseValue(conflict.Value!);
            Assert.True(root.GetProperty("duplicate").GetBoolean());
            Assert.False(root.GetProperty("noChange").GetBoolean());
            Assert.Equal(duplicate.FileId, root.GetProperty("existingFileId").GetGuid());

            mocks.UploadUseCase.Verify(x => x.UploadAsync(It.IsAny<UploadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            mocks.IngestionJobs.Verify(x => x.EnqueueUploadedFile(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    [Fact]
    public async Task Upload_returns_no_change_conflict_when_update_matches_current_content()
    {
        var fileId = Guid.NewGuid();
        var current = new FileMetadata(new FileDescriptor(fileId, "report.pdf"), "application/pdf", FileBytes.Length, DateTimeOffset.UtcNow, contentHash: FileHash);
        var mocks = CreateMocks();
        mocks.MetadataRepository
            .Setup(x => x.GetAsync(It.IsAny<FileDescriptor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileMetadata>.Success(current));

        var controller = CreateController(mocks);
        try
        {
            var result = await controller.Upload(
                Guid.NewGuid(),
                "report.pdf",
                "application/pdf",
                FileBytes.Length,
                fileId,
                CreateFormFile(),
                CancellationToken.None);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            var root = ParseValue(conflict.Value!);
            Assert.True(root.GetProperty("duplicate").GetBoolean());
            Assert.True(root.GetProperty("noChange").GetBoolean());
            Assert.Equal(fileId, root.GetProperty("existingFileId").GetGuid());

            mocks.UploadUseCase.Verify(x => x.UploadAsync(It.IsAny<UploadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            mocks.IngestionJobs.Verify(x => x.EnqueueUploadedFile(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    [Fact]
    public async Task Upload_returns_conflict_when_update_matches_a_different_document()
    {
        var fileId = Guid.NewGuid();
        var current = new FileMetadata(new FileDescriptor(fileId, "report.pdf"), "application/pdf", 9, DateTimeOffset.UtcNow, contentHash: "oldhash");
        var duplicate = new DuplicateUpload(Guid.NewGuid(), "other.pdf", DateTimeOffset.UtcNow, null, FileBytes.Length);
        var mocks = CreateMocks();
        mocks.MetadataRepository
            .Setup(x => x.GetAsync(It.IsAny<FileDescriptor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileMetadata>.Success(current));
        mocks.DuplicateDetection
            .Setup(x => x.FindDuplicateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(duplicate);

        var controller = CreateController(mocks);
        try
        {
            var result = await controller.Upload(
                Guid.NewGuid(),
                "report.pdf",
                "application/pdf",
                FileBytes.Length,
                fileId,
                CreateFormFile(),
                CancellationToken.None);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            var root = ParseValue(conflict.Value!);
            Assert.True(root.GetProperty("duplicate").GetBoolean());
            Assert.False(root.GetProperty("noChange").GetBoolean());
            Assert.Equal(duplicate.FileId, root.GetProperty("existingFileId").GetGuid());
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    [Fact]
    public async Task Upload_returns_bad_request_when_existing_document_not_found()
    {
        var mocks = CreateMocks();
        mocks.MetadataRepository
            .Setup(x => x.GetAsync(It.IsAny<FileDescriptor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileMetadata>.Failure("not found"));

        var controller = CreateController(mocks);
        try
        {
            var result = await controller.Upload(
                Guid.NewGuid(),
                "report.pdf",
                "application/pdf",
                FileBytes.Length,
                Guid.NewGuid(),
                CreateFormFile(),
                CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            mocks.UploadUseCase.Verify(x => x.UploadAsync(It.IsAny<UploadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    [Fact]
    public async Task Upload_creates_version_and_ingests_when_update_content_changed()
    {
        var fileId = Guid.NewGuid();
        var current = new FileMetadata(new FileDescriptor(fileId, "report.pdf"), "application/pdf", 9, DateTimeOffset.UtcNow, contentHash: "oldhash");
        var updated = new FileMetadata(new FileDescriptor(fileId, "report.pdf"), "application/pdf", FileBytes.Length, DateTimeOffset.UtcNow, contentHash: FileHash);
        var jobId = Guid.NewGuid();
        var job = new IngestionJobSnapshot(
            jobId,
            "Upload",
            "Upload report.pdf",
            "Queued",
            "Queued",
            0,
            0,
            1,
            string.Empty,
            fileId,
            "report.pdf",
            DateTimeOffset.UtcNow,
            null,
            DateTimeOffset.UtcNow,
            null,
            null);

        var mocks = CreateMocks();
        mocks.MetadataRepository
            .Setup(x => x.GetAsync(It.IsAny<FileDescriptor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileMetadata>.Success(current));
        mocks.DuplicateDetection
            .Setup(x => x.FindDuplicateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DuplicateUpload?)null);
        mocks.VersioningUseCase
            .Setup(x => x.CreateVersionAsync(It.IsAny<FileDescriptor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileDescriptor>.Success(new FileDescriptor(fileId, "report.pdf", "abc12345")));
        mocks.UploadUseCase
            .Setup(x => x.UploadAsync(It.IsAny<UploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UploadResponse>.Success(new UploadResponse(updated)));
        mocks.IngestionJobs
            .Setup(x => x.EnqueueUploadedFile(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
            .Returns(job);

        var controller = CreateController(mocks);
        try
        {
            var result = await controller.Upload(
                Guid.NewGuid(),
                "report.pdf",
                "application/pdf",
                FileBytes.Length,
                fileId,
                CreateFormFile(),
                CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var root = ParseValue(ok.Value!);
            Assert.Equal(jobId, root.GetProperty("IngestionJobId").GetGuid());
            Assert.Equal("abc12345", root.GetProperty("UpdatedVersion").GetString());
            Assert.Equal(fileId, root.GetProperty("Metadata").GetProperty("Descriptor").GetProperty("FileId").GetGuid());

            mocks.VersioningUseCase.Verify(x => x.CreateVersionAsync(
                It.Is<FileDescriptor>(d => d.FileId == fileId && d.Version == null),
                It.IsAny<CancellationToken>()), Times.Once);
            mocks.UploadUseCase.Verify(x => x.UploadAsync(
                It.Is<UploadRequest>(r => r.Descriptor.FileId == fileId && r.Descriptor.Version == null && r.ContentHash == FileHash),
                It.IsAny<CancellationToken>()), Times.Once);
            mocks.IngestionJobs.Verify(x => x.EnqueueUploadedFile(
                fileId,
                "report.pdf",
                It.IsAny<string>(),
                It.IsAny<string>(),
                FileBytes.Length), Times.Once);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    [Fact]
    public async Task Upload_stores_new_document_and_queues_ingestion_when_no_duplicate()
    {
        var fileId = Guid.NewGuid();
        var metadata = new FileMetadata(new FileDescriptor(fileId, "new.pdf"), "application/pdf", FileBytes.Length, DateTimeOffset.UtcNow, contentHash: FileHash);
        var jobId = Guid.NewGuid();
        var job = new IngestionJobSnapshot(
            jobId,
            "Upload",
            "Upload new.pdf",
            "Queued",
            "Queued",
            0,
            0,
            1,
            string.Empty,
            fileId,
            "new.pdf",
            DateTimeOffset.UtcNow,
            null,
            DateTimeOffset.UtcNow,
            null,
            null);

        var mocks = CreateMocks();
        mocks.DuplicateDetection
            .Setup(x => x.FindDuplicateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DuplicateUpload?)null);
        mocks.UploadUseCase
            .Setup(x => x.UploadAsync(It.IsAny<UploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UploadResponse>.Success(new UploadResponse(metadata)));
        mocks.IngestionJobs
            .Setup(x => x.EnqueueUploadedFile(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
            .Returns(job);

        var controller = CreateController(mocks);
        try
        {
            var result = await controller.Upload(
                fileId,
                "new.pdf",
                "application/pdf",
                FileBytes.Length,
                null,
                CreateFormFile(),
                CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var root = ParseValue(ok.Value!);
            Assert.Equal(jobId, root.GetProperty("IngestionJobId").GetGuid());
            Assert.Equal(fileId, root.GetProperty("Metadata").GetProperty("Descriptor").GetProperty("FileId").GetGuid());

            mocks.UploadUseCase.Verify(x => x.UploadAsync(
                It.Is<UploadRequest>(r => r.Descriptor.FileId == fileId && r.ContentHash == FileHash),
                It.IsAny<CancellationToken>()), Times.Once);
            mocks.IngestionJobs.Verify(x => x.EnqueueUploadedFile(
                fileId,
                "new.pdf",
                It.IsAny<string>(),
                It.IsAny<string>(),
                FileBytes.Length), Times.Once);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    [Fact]
    public async Task Upload_returns_bad_request_when_file_is_null()
    {
        var controller = CreateController(CreateMocks());

        var result = await controller.Upload(
            Guid.NewGuid(),
            "new.pdf",
            "application/pdf",
            FileBytes.Length,
            null,
            null!,
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private static JsonElement ParseValue(object value)
    {
        var json = JsonSerializer.Serialize(value);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static FilesController CreateController(ControllerMocks mocks)
    {
        return new FilesController(
            mocks.UploadUseCase.Object,
            mocks.DownloadUseCase.Object,
            mocks.DeleteUseCase.Object,
            mocks.VersioningUseCase.Object,
            mocks.MetadataRepository.Object,
            mocks.VectorStore.Object,
            mocks.KnowledgeIndexer.Object,
            mocks.DuplicateDetection.Object,
            mocks.IngestionJobs.Object);
    }

    private static ControllerMocks CreateMocks()
    {
        return new ControllerMocks
        {
            UploadUseCase = new Mock<IUploadUseCase>(),
            DownloadUseCase = new Mock<IDownloadUseCase>(),
            DeleteUseCase = new Mock<IDeleteUseCase>(),
            VersioningUseCase = new Mock<IVersioningUseCase>(),
            MetadataRepository = new Mock<IMetadataRepository>(),
            VectorStore = new Mock<IVectorStore>(),
            KnowledgeIndexer = new Mock<IUploadedContentKnowledgeIndexer>(),
            DuplicateDetection = new Mock<IDuplicateDetectionService>(),
            IngestionJobs = new Mock<IIngestionJobService>()
        };
    }

    private static IFormFile CreateFormFile()
    {
        var stream = new MemoryStream(FileBytes);
        return new FormFile(stream, 0, FileBytes.Length, "file", "report.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }

    private static void CleanupTempFiles()
    {
        try
        {
            if (Directory.Exists(TempDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(TempDirectory))
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
        }
    }

    private sealed class ControllerMocks
    {
        public Mock<IUploadUseCase> UploadUseCase { get; set; } = null!;
        public Mock<IDownloadUseCase> DownloadUseCase { get; set; } = null!;
        public Mock<IDeleteUseCase> DeleteUseCase { get; set; } = null!;
        public Mock<IVersioningUseCase> VersioningUseCase { get; set; } = null!;
        public Mock<IMetadataRepository> MetadataRepository { get; set; } = null!;
        public Mock<IVectorStore> VectorStore { get; set; } = null!;
        public Mock<IUploadedContentKnowledgeIndexer> KnowledgeIndexer { get; set; } = null!;
        public Mock<IDuplicateDetectionService> DuplicateDetection { get; set; } = null!;
        public Mock<IIngestionJobService> IngestionJobs { get; set; } = null!;
    }
}
