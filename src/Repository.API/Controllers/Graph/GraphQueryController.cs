using Aletheia.RAGS.Abstractions.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers.Graph;

[ApiController]
[Route("api/graph/query")]
[Authorize]
public class GraphQueryController : ControllerBase
{
    private readonly IGraphQueryService _graphQueryService;
    private readonly IGraphAnalyticsService _graphAnalyticsService;
    private readonly IInternalSearchGate _internalSearchGate;

    public GraphQueryController(
        IGraphQueryService graphQueryService,
        IGraphAnalyticsService graphAnalyticsService,
        IInternalSearchGate internalSearchGate)
    {
        _graphQueryService = graphQueryService ?? throw new ArgumentNullException(nameof(graphQueryService));
        _graphAnalyticsService = graphAnalyticsService ?? throw new ArgumentNullException(nameof(graphAnalyticsService));
        _internalSearchGate = internalSearchGate ?? throw new ArgumentNullException(nameof(internalSearchGate));
    }

    [HttpGet("search/nodes")]
    public async Task<IActionResult> SearchNodes([FromQuery] string query, CancellationToken cancellationToken = default)
    {
        var gate = GateInternalSearch();
        if (gate is not null)
        {
            return gate;
        }

        var result = await _graphQueryService.SearchNodesAsync(query, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpGet("search/relationships")]
    public async Task<IActionResult> SearchRelationships([FromQuery] string query, CancellationToken cancellationToken = default)
    {
        var gate = GateInternalSearch();
        if (gate is not null)
        {
            return gate;
        }

        var result = await _graphQueryService.SearchRelationshipsAsync(query, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpGet("traverse")]
    public async Task<IActionResult> Traverse([FromQuery] string startNodeId, [FromQuery] int depth = 2, CancellationToken cancellationToken = default)
    {
        var gate = GateInternalSearch();
        if (gate is not null)
        {
            return gate;
        }

        var result = await _graphQueryService.TraverseAsync(startNodeId, depth, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpGet("paths")]
    public async Task<IActionResult> FindPaths([FromQuery] string fromNodeId, [FromQuery] string toNodeId, CancellationToken cancellationToken = default)
    {
        var gate = GateInternalSearch();
        if (gate is not null)
        {
            return gate;
        }

        var result = await _graphQueryService.FindPathsAsync(fromNodeId, toNodeId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpGet("neighborhood")]
    public async Task<IActionResult> GetNeighborhood([FromQuery] string nodeId, [FromQuery] int depth = 2, CancellationToken cancellationToken = default)
    {
        var gate = GateInternalSearch();
        if (gate is not null)
        {
            return gate;
        }

        var result = await _graphQueryService.GetNeighborhoodAsync(nodeId, depth, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpGet("entity/{entityId}")]
    public async Task<IActionResult> GetEntityGraph([FromRoute] string entityId, CancellationToken cancellationToken = default)
    {
        var gate = GateInternalSearch();
        if (gate is not null)
        {
            return gate;
        }

        var result = await _graphQueryService.GetEntityGraphAsync(entityId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpGet("analytics/metrics")]
    public async Task<IActionResult> GetMetrics(CancellationToken cancellationToken = default)
    {
        var gate = GateInternalSearch();
        if (gate is not null)
        {
            return gate;
        }

        var result = await _graphAnalyticsService.ComputeGraphMetricsAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpGet("analytics/health")]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken = default)
    {
        var gate = GateInternalSearch();
        if (gate is not null)
        {
            return gate;
        }

        var result = await _graphAnalyticsService.ComputeGraphHealthAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    private IActionResult? GateInternalSearch()
    {
        return _internalSearchGate.ShowInternalSearch
            ? null
            : NotFound(new { error = "Not found." });
    }
}
