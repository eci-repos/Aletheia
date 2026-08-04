using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers;

[ApiController]
[Route("api/graphrag")]
[Authorize]
public class GraphRagController : ControllerBase
{
    private readonly IGraphRagService _graphRagService;
    private readonly IInternalSearchGate _internalSearchGate;

    public GraphRagController(IGraphRagService graphRagService, IInternalSearchGate internalSearchGate)
    {
        _graphRagService = graphRagService ?? throw new ArgumentNullException(nameof(graphRagService));
        _internalSearchGate = internalSearchGate ?? throw new ArgumentNullException(nameof(internalSearchGate));
    }

    [HttpPost("ingest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IngestAsync(
        [FromBody] IngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _graphRagService.IngestAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok();
    }

    [HttpGet("retrieve")]
    [ProducesResponseType(typeof(IReadOnlyList<Aletheia.RAGS.Abstractions.Models.SearchResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Retrieve(
        [FromQuery] string query,
        [FromQuery] int topK = 5,
        [FromQuery] int maxExpanded = 10,
        CancellationToken cancellationToken = default)
    {
        var gate = GateInternalSearch();
        if (gate is not null)
        {
            return gate;
        }

        var result = await _graphRagService.RetrieveAsync(query, topK, maxExpanded, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpGet("global")]
    [ProducesResponseType(typeof(GlobalSearchResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GlobalSearch(
        [FromQuery] string query,
        CancellationToken cancellationToken = default)
    {
        var gate = GateInternalSearch();
        if (gate is not null)
        {
            return gate;
        }

        var result = await _graphRagService.GlobalSearchAsync(query, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    private IActionResult? GateInternalSearch()
    {
        return _internalSearchGate.ShowInternalSearch
            ? null
            : NotFound(new { error = "Not found." });
    }
}
