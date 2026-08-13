using System.Security.Cryptography;
using Aletheia.Foundation.Security;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.API.Services;
using Aletheia.Repository.Application;
using Aletheia.Repository.Domain.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly IUploadUseCase _uploadUseCase;
    private readonly IDownloadUseCase _downloadUseCase;
    private readonly IDeleteUseCase _deleteUseCase;
    private readonly IVersioningUseCase _versioningUseCase;
    private readonly IMetadataRepository _metadataRepository;
    private readonly IVectorStore _vectorStore;
    private readonly IUploadedContentKnowledgeIndexer _knowledgeIndexer;
    private readonly IDuplicateDetectionService _duplicateDetection;
    private readonly IIngestionJobService _ingestionJobs;
    private readonly IUploadedFileTextExtractor _textExtractor;

    public FilesController(
        IUploadUseCase uploadUseCase,
        IDownloadUseCase downloadUseCase,
        IDeleteUseCase deleteUseCase,
        IVersioningUseCase versioningUseCase,
        IMetadataRepository metadataRepository,
        IVectorStore vectorStore,
        IUploadedContentKnowledgeIndexer knowledgeIndexer,
        IDuplicateDetectionService duplicateDetection,
        IIngestionJobService ingestionJobs,
        IUploadedFileTextExtractor textExtractor)
    {
        _uploadUseCase = uploadUseCase ?? throw new ArgumentNullException(nameof(uploadUseCase));
        _downloadUseCase = downloadUseCase ?? throw new ArgumentNullException(nameof(downloadUseCase));
        _deleteUseCase = deleteUseCase ?? throw new ArgumentNullException(nameof(deleteUseCase));
        _versioningUseCase = versioningUseCase ?? throw new ArgumentNullException(nameof(versioningUseCase));
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _knowledgeIndexer = knowledgeIndexer ?? throw new ArgumentNullException(nameof(knowledgeIndexer));
        _duplicateDetection = duplicateDetection ?? throw new ArgumentNullException(nameof(duplicateDetection));
        _ingestionJobs = ingestionJobs ?? throw new ArgumentNullException(nameof(ingestionJobs));
        _textExtractor = textExtractor ?? throw new ArgumentNullException(nameof(textExtractor));
    }

    [HttpPost("upload")]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Upload(
        [FromForm] Guid fileId,
        [FromForm] string fileName,
        [FromForm] string contentType,
        [FromForm] long sizeBytes,
        [FromForm] Guid? existingFileId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return BadRequest(new { error = "Uploaded file is required." });
        }

        var tempPath = await CopyToTemporaryFileAsync(file, cancellationToken).ConfigureAwait(false);
        var contentHash = await ComputeSha256Async(tempPath, cancellationToken).ConfigureAwait(false);

        if (existingFileId.HasValue && existingFileId.Value != Guid.Empty)
        {
            return await UploadUpdateAsync(
                existingFileId.Value,
                fileName,
                contentType,
                sizeBytes,
                tempPath,
                contentHash,
                cancellationToken).ConfigureAwait(false);
        }

        // New document upload. Trap exact duplicates before anything is stored or ingested.
        var duplicate = await _duplicateDetection.FindDuplicateAsync(contentHash, cancellationToken).ConfigureAwait(false);
        if (duplicate is not null)
        {
            TryDeleteTempFile(tempPath);
            return Conflict(new
            {
                duplicate = true,
                noChange = false,
                message = $"This exact file is already in the repository (uploaded {duplicate.UploadedAt:g} as '{duplicate.FileName}'). Nothing was uploaded.",
                existingFileId = duplicate.FileId,
                existingFileName = duplicate.FileName,
                existingUploadedAt = duplicate.UploadedAt,
                existingVersion = duplicate.Version
            });
        }

        var descriptor = new FileDescriptor(fileId, fileName);
        FileMetadata uploadedMetadata;
        await using (var uploadStream = System.IO.File.OpenRead(tempPath))
        {
            var request = new UploadRequest(descriptor, uploadStream, contentType, sizeBytes, contentHash: contentHash);
            var result = await _uploadUseCase.UploadAsync(request, cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                TryDeleteTempFile(tempPath);
                return BadRequest(new { error = result.Error });
            }

            uploadedMetadata = result.Value!.Metadata;
        }

        var job = _ingestionJobs.EnqueueUploadedFile(fileId, fileName, contentType, tempPath, sizeBytes);

        return Ok(new
        {
            Metadata = uploadedMetadata,
            RagsIngested = false,
            KnowledgeIndexed = false,
            IngestionStatus = "Queued",
            IngestionError = (string?)null,
            IngestionJobId = job.JobId
        });
    }

    private async Task<IActionResult> UploadUpdateAsync(
        Guid existingFileId,
        string submittedFileName,
        string contentType,
        long sizeBytes,
        string tempPath,
        string contentHash,
        CancellationToken cancellationToken)
    {
        // Resolve the current (unversioned) document.
        var existing = await _metadataRepository
            .GetAsync(new FileDescriptor(existingFileId, submittedFileName), cancellationToken)
            .ConfigureAwait(false);

        if (existing.IsFailure || existing.Value is null)
        {
            TryDeleteTempFile(tempPath);
            return BadRequest(new { error = $"Existing document {existingFileId} was not found. Cannot update a document that does not exist." });
        }

        var current = existing.Value;

        // No-change update: same content as the current version.
        if (!string.IsNullOrWhiteSpace(current.ContentHash) &&
            string.Equals(current.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteTempFile(tempPath);
            return Conflict(new
            {
                duplicate = true,
                noChange = true,
                message = $"'{current.Descriptor.FileName}' is already up to date with this exact content. No new version was created.",
                existingFileId,
                existingFileName = current.Descriptor.FileName,
                existingUploadedAt = current.UploadedAt,
                existingVersion = current.Descriptor.Version
            });
        }

        // Exact duplicate of a *different* document is still a duplicate trap.
        var duplicate = await _duplicateDetection.FindDuplicateAsync(contentHash, cancellationToken).ConfigureAwait(false);
        if (duplicate is not null && duplicate.FileId != existingFileId)
        {
            TryDeleteTempFile(tempPath);
            return Conflict(new
            {
                duplicate = true,
                noChange = false,
                message = $"This exact file is already in the repository as '{duplicate.FileName}' (uploaded {duplicate.UploadedAt:g}) under a different document. Nothing was uploaded.",
                existingFileId = duplicate.FileId,
                existingFileName = duplicate.FileName,
                existingUploadedAt = duplicate.UploadedAt,
                existingVersion = duplicate.Version
            });
        }

        // Snapshot the current state as a named version, then replace the current (unversioned) row and blob.
        var versionResult = await _versioningUseCase
            .CreateVersionAsync(new FileDescriptor(existingFileId, current.Descriptor.FileName), cancellationToken)
            .ConfigureAwait(false);
        if (versionResult.IsFailure)
        {
            TryDeleteTempFile(tempPath);
            return BadRequest(new { error = versionResult.Error });
        }

        var descriptor = new FileDescriptor(existingFileId, current.Descriptor.FileName);
        FileMetadata uploadedMetadata;
        await using (var uploadStream = System.IO.File.OpenRead(tempPath))
        {
            var request = new UploadRequest(descriptor, uploadStream, contentType, sizeBytes, contentHash: contentHash);
            var result = await _uploadUseCase.UploadAsync(request, cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                TryDeleteTempFile(tempPath);
                return BadRequest(new { error = result.Error });
            }

            uploadedMetadata = result.Value!.Metadata;
        }

        var job = _ingestionJobs.EnqueueUploadedFile(existingFileId, current.Descriptor.FileName, contentType, tempPath, sizeBytes);

        return Ok(new
        {
            Metadata = uploadedMetadata,
            UpdatedVersion = versionResult.Value.Version,
            RagsIngested = false,
            KnowledgeIndexed = false,
            IngestionStatus = "Queued",
            IngestionError = (string?)null,
            IngestionJobId = job.JobId
        });
    }

    [HttpGet("duplicates")]
    [Authorize(Roles = RoleDefinitions.Administrator)]
    [ProducesResponseType(typeof(IReadOnlyList<FileMetadata>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListDuplicates(CancellationToken cancellationToken)
    {
        var result = await _metadataRepository.ListContentHashDuplicatesAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpGet("download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(
        [FromQuery] Guid fileId,
        [FromQuery] string fileName,
        [FromQuery] string? version,
        CancellationToken cancellationToken)
    {
        var descriptor = new FileDescriptor(fileId, fileName, version);
        var request = new DownloadRequest(descriptor);

        var result = await _downloadUseCase.DownloadAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return NotFound(new { error = result.Error });
        }

        return File(result.Value!.Content, result.Value.Metadata.ContentType, result.Value.Metadata.Descriptor.FileName);
    }

    [HttpGet("{id:guid}/preview")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileTextPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> Preview(
        Guid id,
        [FromQuery] string? version,
        CancellationToken cancellationToken)
    {
        // Resolve the current row by file id alone — the viewer only knows the source id.
        var metadataResult = await _metadataRepository
            .GetByFileIdAsync(id, version, cancellationToken)
            .ConfigureAwait(false);
        if (metadataResult.IsFailure || metadataResult.Value is null)
        {
            return NotFound(new { error = $"File {id} was not found." });
        }

        var metadata = metadataResult.Value;
        var descriptor = new FileDescriptor(id, metadata.Descriptor.FileName, version);

        var downloadResult = await _downloadUseCase
            .DownloadAsync(new DownloadRequest(descriptor), cancellationToken)
            .ConfigureAwait(false);
        if (downloadResult.IsFailure || downloadResult.Value is null)
        {
            return NotFound(new { error = downloadResult.Error });
        }

        // PDF streams the raw blob so the browser can render it with PDF.js (text layer).
        if (UploadedFileTextExtractor.IsPdf(descriptor.FileName, metadata.ContentType))
        {
            return File(downloadResult.Value.Content, "application/pdf", enableRangeProcessing: true);
        }

        // Text-like and Office types render the extracted text with page markers.
        var extraction = await _textExtractor
            .ExtractAsync(descriptor.FileName, metadata.ContentType, downloadResult.Value.Content, cancellationToken)
            .ConfigureAwait(false);
        if (extraction.IsFailure)
        {
            return BadRequest(new { error = extraction.Error });
        }

        if (!extraction.Value.IsSupported || extraction.Value.Text is null)
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new
            {
                error = $"Preview is not supported for {metadata.ContentType}."
            });
        }

        return Ok(new FileTextPreviewResponse(
            descriptor.FileName,
            metadata.ContentType,
            extraction.Value.Text,
            extraction.Value.Pages));
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        [FromQuery] Guid fileId,
        [FromQuery] string fileName,
        [FromQuery] string? version,
        CancellationToken cancellationToken)
    {
        var descriptor = new FileDescriptor(fileId, fileName, version);

        var vectorResult = await _vectorStore.DeleteBySourceAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (vectorResult.IsFailure)
        {
            return BadRequest(new { error = vectorResult.Error });
        }

        var knowledgeResult = await _knowledgeIndexer.DeleteSourceAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (knowledgeResult.IsFailure)
        {
            return BadRequest(new { error = knowledgeResult.Error });
        }

        var result = await _deleteUseCase.DeleteAsync(new DeleteRequest(descriptor), cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return NotFound(new { error = result.Error });
        }

        return NoContent();
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = System.IO.File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<string> CopyToTemporaryFileAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "aletheia-ingestion");
        Directory.CreateDirectory(directory);

        var extension = Path.GetExtension(file.FileName);
        var safeExtension = extension.Length <= 16 ? extension : string.Empty;
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}{safeExtension}");

        await using var input = file.OpenReadStream();
        await using var output = System.IO.File.Create(path);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch
        {
        }
    }
}

