using Aletheia.KnowledgeGraph.Abstractions.Interfaces;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.KnowledgeGraph.Infrastructure.Neo4j.GraphStore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers;

[ApiController]
[Route("api/graph")]
[Authorize]
public class KnowledgeGraphController : ControllerBase
{
    private readonly IGraphService _graphService;
    private readonly GraphSyncService _syncService;

    public KnowledgeGraphController(IGraphService graphService, GraphSyncService syncService)
    {
        _graphService = graphService ?? throw new ArgumentNullException(nameof(graphService));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
    }

    [HttpPost("import")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportFromOntology(CancellationToken cancellationToken)
    {
        var result = await _syncService.SyncFromOntologyAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new { message = "Graph imported successfully." });
    }

    [HttpGet("nodes")]
    [ProducesResponseType(typeof(IReadOnlyList<GraphNode>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetNodes(CancellationToken cancellationToken)
    {
        var result = await _graphService.GetNodesAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpGet("edges")]
    [ProducesResponseType(typeof(IReadOnlyList<GraphEdge>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetEdges(CancellationToken cancellationToken)
    {
        var result = await _graphService.GetEdgesAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpGet("nodes/{id}/neighbors")]
    [ProducesResponseType(typeof(IReadOnlyList<GraphNode>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetNeighbors(string id, CancellationToken cancellationToken)
    {
        var result = await _graphService.GetNeighborsAsync(id, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpGet("path")]
    [ProducesResponseType(typeof(IReadOnlyList<GraphPath>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> FindPath(
        [FromQuery] string from,
        [FromQuery] string to,
        CancellationToken cancellationToken)
    {
        var result = await _graphService.FindShortestPathAsync(from, to, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }
}
