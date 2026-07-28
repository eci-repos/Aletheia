using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers;

[ApiController]
[Route("api/lazygraphrag")]
[Authorize]
public class LazyGraphRagController : ControllerBase
{
    private readonly ILazyGraphRagService _lazyGraphRagService;

    public LazyGraphRagController(ILazyGraphRagService lazyGraphRagService)
    {
        _lazyGraphRagService = lazyGraphRagService ?? throw new ArgumentNullException(nameof(lazyGraphRagService));
    }

    [HttpPost("ingest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Ingest([FromBody] IngestionRequest request, CancellationToken cancellationToken)
    {
        var result = await _lazyGraphRagService.IngestAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok();
    }

    [HttpGet("retrieve")]
    [ProducesResponseType(typeof(IReadOnlyList<Aletheia.RAGS.Abstractions.Models.SearchResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Retrieve(
        [FromQuery] string query,
        [FromQuery] int topK = 5,
        [FromQuery] int maxExpanded = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _lazyGraphRagService.RetrieveAsync(query, topK, maxExpanded, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpGet("global")]
    [ProducesResponseType(typeof(GlobalSearchResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GlobalSearch(
        [FromQuery] string query,
        CancellationToken cancellationToken = default)
    {
        var result = await _lazyGraphRagService.GlobalSearchAsync(query, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }
}
