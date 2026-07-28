using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers;

[ApiController]
[Route("api/wiki")]
[Authorize]
public sealed class WikiController : ControllerBase
{
    private readonly IWragsWikiService _wikiService;
    private readonly IIngestionJobService _jobs;

    public WikiController(IWragsWikiService wikiService, IIngestionJobService jobs)
    {
        _wikiService = wikiService ?? throw new ArgumentNullException(nameof(wikiService));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    [HttpGet("search")]
    [ProducesResponseType(typeof(IReadOnlyList<WikiPage>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] string query,
        [FromQuery] string mode = "wrags",
        [FromQuery] int topK = 6,
        [FromQuery] int expansion = 1,
        [FromQuery] bool regenerate = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _wikiService.SearchAsync(
            new WikiSearchRequest
            {
                Query = query,
                Mode = mode,
                TopK = topK,
                Expansion = expansion,
                Regenerate = regenerate
            },
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpPost("regenerate")]
    [ProducesResponseType(typeof(IReadOnlyList<WikiPage>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RegenerateAsync(
        [FromBody] WikiSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _wikiService.RegenerateAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpPost("regenerate/job")]
    [ProducesResponseType(typeof(IngestionJobSnapshot), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult RegenerateJob([FromBody] WikiSearchRequest request)
    {
        try
        {
            return Accepted(_jobs.EnqueueWikiRegeneration(request));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("retrieve")]
    [ProducesResponseType(typeof(IReadOnlyList<SearchResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RetrieveAsync(
        [FromQuery] string query,
        [FromQuery] string mode = "wrags",
        [FromQuery] int topK = 6,
        [FromQuery] int expansion = 1,
        CancellationToken cancellationToken = default)
    {
        var result = await _wikiService.RetrieveAsync(
            new WikiSearchRequest
            {
                Query = query,
                Mode = mode,
                TopK = topK,
                Expansion = expansion
            },
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpGet("recent")]
    [ProducesResponseType(typeof(IReadOnlyList<WikiPage>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecentAsync(
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _wikiService.GetRecentAsync(take, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpGet("pages/{pageId:guid}")]
    [ProducesResponseType(typeof(WikiPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAsync(
        Guid pageId,
        CancellationToken cancellationToken = default)
    {
        var result = await _wikiService.GetAsync(pageId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return result.Value is null ? NotFound() : Ok(result.Value);
    }

    [HttpGet("pages/{pageId:guid}/related")]
    [ProducesResponseType(typeof(IReadOnlyList<WikiPageLink>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RelatedAsync(
        Guid pageId,
        [FromQuery] int take = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _wikiService.GetRelatedAsync(pageId, take, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpGet("pages/{pageId:guid}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<WikiPageHistoryEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HistoryAsync(
        Guid pageId,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _wikiService.GetHistoryAsync(pageId, take, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpPatch("pages/{pageId:guid}/status")]
    [ProducesResponseType(typeof(WikiPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateStatusAsync(
        Guid pageId,
        [FromBody] WikiPageStatusUpdate update,
        CancellationToken cancellationToken = default)
    {
        var userName = User.Identity?.Name ?? "system";
        var result = await _wikiService.UpdateStatusAsync(
            pageId,
            update with { ReviewedBy = update.ReviewedBy ?? userName },
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return result.Value is null ? NotFound() : Ok(result.Value);
    }

    [HttpPut("pages/{pageId:guid}")]
    [ProducesResponseType(typeof(WikiPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePageAsync(
        Guid pageId,
        [FromBody] WikiPageEditRequest request,
        CancellationToken cancellationToken = default)
    {
        var userName = User.Identity?.Name ?? "system";
        request.EditedBy ??= userName;
        var result = await _wikiService.UpdatePageAsync(pageId, request, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return result.Value is null ? NotFound() : Ok(result.Value);
    }
}
