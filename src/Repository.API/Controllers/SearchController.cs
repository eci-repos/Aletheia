using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.API.Services;
using Aletheia.Repository.Domain.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly ISearchUseCase _searchUseCase;
    private readonly IVectorStore _vectorStore;
    private readonly IIngestionJobService _ingestionJobs;

    public SearchController(ISearchUseCase searchUseCase, IVectorStore vectorStore, IIngestionJobService ingestionJobs)
    {
        _searchUseCase = searchUseCase ?? throw new ArgumentNullException(nameof(searchUseCase));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _ingestionJobs = ingestionJobs ?? throw new ArgumentNullException(nameof(ingestionJobs));
    }

    [HttpGet]
    [ProducesResponseType(typeof(SearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromQuery] string? query,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var request = new SearchRequest(query, pageNumber, pageSize);
        var result = await _searchUseCase.SearchAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        // Sprint 69: stamp each file with its RAGS chunk count so the Repository Browser can show
        // whether ingestion completed (a file can be uploaded but have no embeddings if its job failed).
        await StampChunkCountsAsync(result.Value!.Results.Items, cancellationToken).ConfigureAwait(false);

        return Ok(result.Value);
    }

    private async Task StampChunkCountsAsync(IReadOnlyList<FileMetadata> files, CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return;
        }

        var sourceIds = files.Select(file => file.Descriptor.FileId).Distinct().ToList();
        var counts = await _vectorStore.GetChunkCountsAsync(sourceIds, cancellationToken).ConfigureAwait(false);
        if (counts.IsFailure || counts.Value is null)
        {
            return;
        }

        foreach (var file in files)
        {
            file.ChunkCount = counts.Value.TryGetValue(file.Descriptor.FileId, out var count) ? count : 0;
            // Sprint 69 post-sprint: an active ingestion job means the source is mid-flight
            // (partial embeddings possible) — surface it as "Processing" rather than a premature
            // green "Ingested". Ground truth for the terminal states stays the embeddings.
            file.IsProcessing = _ingestionJobs.HasActiveIngestion(file.Descriptor.FileId);
        }
    }
}
