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

    public RagsController(IRagsService ragsService, IRagsStatusService ragsStatusService)
    {
        _ragsService = ragsService ?? throw new ArgumentNullException(nameof(ragsService));
        _ragsStatusService = ragsStatusService ?? throw new ArgumentNullException(nameof(ragsStatusService));
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
        CancellationToken cancellationToken = default)
    {
        var request = new RetrievalRequest(query, topK);
        var result = await _ragsService.RetrieveAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }
}
