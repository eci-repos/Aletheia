using Aletheia.RAGS.Abstractions.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers;

[ApiController]
[Route("api/communities")]
[Authorize]
public class CommunityController : ControllerBase
{
    private readonly ICommunityDetectionService _communityDetection;

    public CommunityController(ICommunityDetectionService communityDetection)
    {
        _communityDetection = communityDetection ?? throw new ArgumentNullException(nameof(communityDetection));
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<GraphCommunity>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken = default)
    {
        var result = await _communityDetection.DiscoverAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpGet("{communityId}")]
    [ProducesResponseType(typeof(GraphCommunity), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string communityId, CancellationToken cancellationToken = default)
    {
        var result = await _communityDetection.GetCommunityAsync(communityId, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        if (result.Value is null)
        {
            return NotFound(new { communityId });
        }

        return Ok(result.Value);
    }

    [HttpPost("detect")]
    [ProducesResponseType(typeof(IReadOnlyList<GraphCommunity>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DetectClusters(CancellationToken cancellationToken = default)
    {
        var result = await _communityDetection.DetectClustersAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpPost("assign")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AssignNode(
        [FromQuery] string nodeId,
        [FromQuery] string communityId,
        CancellationToken cancellationToken = default)
    {
        var result = await _communityDetection.AssignAsync(nodeId, communityId, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new { nodeId, communityId });
    }

    [HttpGet("node/{nodeId}")]
    [ProducesResponseType(typeof(IReadOnlyList<GraphCommunity>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetByNode(string nodeId, CancellationToken cancellationToken = default)
    {
        var result = await _communityDetection.GetCommunitiesForNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }
}