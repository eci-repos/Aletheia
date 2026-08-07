using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers;

[ApiController]
[Route("api/rags")]
[Authorize]
public class RagsController : ControllerBase
{
    private readonly IRagsService _ragsService;
    private readonly IRagsStatusService _ragsStatusService;
    private readonly IKnowledgeThemeService? _themeService;

    public RagsController(IRagsService ragsService, IRagsStatusService ragsStatusService, IKnowledgeThemeService? themeService = null)
    {
        _ragsService = ragsService ?? throw new ArgumentNullException(nameof(ragsService));
        _ragsStatusService = ragsStatusService ?? throw new ArgumentNullException(nameof(ragsStatusService));
        _themeService = themeService;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(RagsStatusSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var result = await _ragsStatusService.GetAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpPost("ingest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Ingest([FromBody] IngestionRequest request, CancellationToken cancellationToken)
    {
        var result = await _ragsService.IngestAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok();
    }

    [HttpGet("retrieve")]
    [ProducesResponseType(typeof(IReadOnlyList<SearchResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Retrieve(
        [FromQuery] string query,
        [FromQuery] int topK = 5,
        [FromQuery] string? themes = null,
        CancellationToken cancellationToken = default)
    {
        var request = new RetrievalRequest(query, topK);

        // Sprint 59: optional shared theme scope (comma-separated theme names) resolved to source ids.
        if (!string.IsNullOrWhiteSpace(themes) && _themeService is not null)
        {
            var themeList = themes
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var resolved = await _themeService
                .ResolveSourceIdsAsync(themeList, cancellationToken)
                .ConfigureAwait(false);
            if (resolved.IsFailure)
            {
                return BadRequest(new { error = resolved.Error });
            }

            request = new RetrievalRequest(query, topK, sourceIds: resolved.Value);
        }

        var result = await _ragsService.RetrieveAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }
}
