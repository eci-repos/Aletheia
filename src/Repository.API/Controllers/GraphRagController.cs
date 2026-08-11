using Aletheia.Foundation.Shared;
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
    private readonly IKnowledgeThemeService? _themeService;

    public GraphRagController(IGraphRagService graphRagService, IInternalSearchGate internalSearchGate, IKnowledgeThemeService? themeService = null)
    {
        _graphRagService = graphRagService ?? throw new ArgumentNullException(nameof(graphRagService));
        _internalSearchGate = internalSearchGate ?? throw new ArgumentNullException(nameof(internalSearchGate));
        _themeService = themeService;
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
        [FromQuery] string? themes = null,
        CancellationToken cancellationToken = default)
    {
        var gate = GateInternalSearch();
        if (gate is not null)
        {
            return gate;
        }

        var sourceIds = await ResolveThemeSourceIdsAsync(themes, cancellationToken).ConfigureAwait(false);
        if (sourceIds.IsFailure)
        {
            return BadRequest(new { error = sourceIds.Error });
        }

        var result = await _graphRagService.RetrieveAsync(query, topK, maxExpanded, cancellationToken, sourceIds.Value).ConfigureAwait(false);

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
        [FromQuery] string? themes = null,
        CancellationToken cancellationToken = default)
    {
        var gate = GateInternalSearch();
        if (gate is not null)
        {
            return gate;
        }

        var sourceIds = await ResolveThemeSourceIdsAsync(themes, cancellationToken).ConfigureAwait(false);
        if (sourceIds.IsFailure)
        {
            return BadRequest(new { error = sourceIds.Error });
        }

        var result = await _graphRagService.GlobalSearchAsync(query, cancellationToken, sourceIds.Value).ConfigureAwait(false);

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

    // Sprint 64: optional shared theme scope (comma-separated theme names) resolved to source ids,
    // following the RagsController pattern. Null when no themes are supplied.
    private async Task<Result<IReadOnlyList<Guid>?>> ResolveThemeSourceIdsAsync(string? themes, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(themes) || _themeService is null)
        {
            return Result<IReadOnlyList<Guid>?>.Success(null);
        }

        var themeList = themes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var resolved = await _themeService
            .ResolveSourceIdsAsync(themeList, cancellationToken)
            .ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyList<Guid>?>.Failure(resolved.Error);
        }

        return Result<IReadOnlyList<Guid>?>.Success(resolved.Value);
    }
}
