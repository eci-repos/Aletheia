using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers;

[ApiController]
[Route("api/summaries")]
[Authorize]
public class SummaryController : ControllerBase
{
    private readonly IGraphSummaryService _graphSummaryService;
    private readonly IHierarchicalSummaryService _hierarchicalSummaryService;

    public SummaryController(
        IGraphSummaryService graphSummaryService,
        IHierarchicalSummaryService hierarchicalSummaryService)
    {
        _graphSummaryService = graphSummaryService ?? throw new ArgumentNullException(nameof(graphSummaryService));
        _hierarchicalSummaryService = hierarchicalSummaryService ?? throw new ArgumentNullException(nameof(hierarchicalSummaryService));
    }

    [HttpPost("entity/{entityId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SummarizeEntity(
        string entityId,
        CancellationToken cancellationToken = default)
    {
        var result = await _graphSummaryService.SummarizeEntityAsync(entityId, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new { entityId, summary = result.Value });
    }

    [HttpPost("community/{communityId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SummarizeCommunity(
        string communityId,
        CancellationToken cancellationToken = default)
    {
        var result = await _graphSummaryService.SummarizeCommunityAsync(communityId, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new { communityId, summary = result.Value });
    }

    [HttpPost("cluster/{clusterId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SummarizeCluster(
        string clusterId,
        CancellationToken cancellationToken = default)
    {
        var result = await _graphSummaryService.SummarizeClusterAsync(clusterId, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new { clusterId, summary = result.Value });
    }

    [HttpPost("global")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SummarizeGlobal(
        CancellationToken cancellationToken = default)
    {
        var result = await _graphSummaryService.SummarizeGlobalAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new { summary = result.Value });
    }

    [HttpPost("hierarchy/entity/{entityId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SummarizeEntityHierarchical(
        string entityId,
        CancellationToken cancellationToken = default)
    {
        var result = await _hierarchicalSummaryService.SummarizeEntityAsync(entityId, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new { entityId, summary = result.Value });
    }

    [HttpPost("hierarchy/community")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SummarizeCommunityHierarchical(
        [FromQuery] string communityId,
        CancellationToken cancellationToken = default)
    {
        var result = await _hierarchicalSummaryService.SummarizeCommunityAsync(communityId, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new { communityId, summary = result.Value });
    }

    [HttpPost("hierarchy/knowledge-area")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SummarizeKnowledgeArea(
        [FromQuery] string areaId,
        CancellationToken cancellationToken = default)
    {
        var result = await _hierarchicalSummaryService.SummarizeKnowledgeAreaAsync(areaId, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new { areaId, summary = result.Value });
    }
}