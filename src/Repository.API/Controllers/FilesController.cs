using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.API.Services;
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
    private readonly IVectorStore _vectorStore;
    private readonly IUploadedContentKnowledgeIndexer _knowledgeIndexer;
    private readonly IIngestionJobService _ingestionJobs;

    public FilesController(
        IUploadUseCase uploadUseCase,
        IDownloadUseCase downloadUseCase,
        IDeleteUseCase deleteUseCase,
        IVectorStore vectorStore,
        IUploadedContentKnowledgeIndexer knowledgeIndexer,
        IIngestionJobService ingestionJobs)
    {
        _uploadUseCase = uploadUseCase ?? throw new ArgumentNullException(nameof(uploadUseCase));
        _downloadUseCase = downloadUseCase ?? throw new ArgumentNullException(nameof(downloadUseCase));
        _deleteUseCase = deleteUseCase ?? throw new ArgumentNullException(nameof(deleteUseCase));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _knowledgeIndexer = knowledgeIndexer ?? throw new ArgumentNullException(nameof(knowledgeIndexer));
        _ingestionJobs = ingestionJobs ?? throw new ArgumentNullException(nameof(ingestionJobs));
    }

    [HttpPost("upload")]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(
        [FromForm] Guid fileId,
        [FromForm] string fileName,
        [FromForm] string contentType,
        [FromForm] long sizeBytes,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return BadRequest(new { error = "Uploaded file is required." });
        }

        var tempPath = await CopyToTemporaryFileAsync(file, cancellationToken).ConfigureAwait(false);
        var descriptor = new FileDescriptor(fileId, fileName);
        await using var uploadStream = System.IO.File.OpenRead(tempPath);
        var request = new UploadRequest(descriptor, uploadStream, contentType, sizeBytes);

        var result = await _uploadUseCase.UploadAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            TryDeleteTempFile(tempPath);
            return BadRequest(new { error = result.Error });
        }

        var job = _ingestionJobs.EnqueueUploadedFile(fileId, fileName, contentType, tempPath, sizeBytes);

        return Ok(new
        {
            result.Value!.Metadata,
            RagsIngested = false,
            KnowledgeIndexed = false,
            IngestionStatus = "Queued",
            IngestionError = (string?)null,
            IngestionJobId = job.JobId
        });
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
