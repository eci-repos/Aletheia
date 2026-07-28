using Aletheia.RAGS.Abstractions.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers.Graph;

[ApiController]
[Route("api/graph/admin")]
[Authorize(Roles = "Administrator,PowerUser")]
public class GraphAdminController : ControllerBase
{
    private readonly IGraphAdminService _graphAdminService;

    public GraphAdminController(IGraphAdminService graphAdminService)
    {
        _graphAdminService = graphAdminService ?? throw new ArgumentNullException(nameof(graphAdminService));
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateGraph(CancellationToken cancellationToken = default)
    {
        var result = await _graphAdminService.ValidateGraphAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok() : BadRequest(new { error = result.Error });
    }

    [HttpPost("rebuild")]
    public async Task<IActionResult> RebuildGraph(CancellationToken cancellationToken = default)
    {
        var result = await _graphAdminService.RebuildGraphAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok() : BadRequest(new { error = result.Error });
    }

    [HttpPost("repair")]
    public async Task<IActionResult> RepairGraph(CancellationToken cancellationToken = default)
    {
        var result = await _graphAdminService.RepairGraphAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok() : BadRequest(new { error = result.Error });
    }

    [HttpPost("merge-duplicates")]
    public async Task<IActionResult> MergeDuplicates(CancellationToken cancellationToken = default)
    {
        var result = await _graphAdminService.MergeDuplicateEntitiesAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok() : BadRequest(new { error = result.Error });
    }

    [HttpPost("recompute-communities")]
    public async Task<IActionResult> RecomputeCommunities(CancellationToken cancellationToken = default)
    {
        var result = await _graphAdminService.RecomputeCommunitiesAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok() : BadRequest(new { error = result.Error });
    }

    [HttpPost("regenerate-summaries")]
    public async Task<IActionResult> RegenerateSummaries(CancellationToken cancellationToken = default)
    {
        var result = await _graphAdminService.RegenerateSummariesAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok() : BadRequest(new { error = result.Error });
    }

    [HttpPost("optimize")]
    public async Task<IActionResult> OptimizeGraph(CancellationToken cancellationToken = default)
    {
        var result = await _graphAdminService.OptimizeGraphAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok() : BadRequest(new { error = result.Error });
    }
}
