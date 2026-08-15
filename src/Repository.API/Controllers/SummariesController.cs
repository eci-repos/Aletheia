using Aletheia.Foundation.Security;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers;

/// <summary>
/// User-facing "Summaries" search surface. GraphRAG and LazyGraphRAG are the same product to the
/// user — only the production of the summaries differs — so this controller exposes one endpoint
/// that resolves to whichever engine can answer, without leaking the internal names. Unlike the
/// internal graph controllers, <c>retrieve</c> is NOT gated by <c>ShowInternalSearch</c>: summaries
/// are a first-class user search mode. The <c>status</c> endpoint (summary coverage + management
/// visibility) is Administrator-only.
/// </summary>
[ApiController]
[Route("api/summaries")]
[Authorize]
public class SummariesController : ControllerBase
{
    private readonly ISummariesRetrievalService _summariesRetrieval;
    private readonly ISummariesStatusService _summariesStatus;
    private readonly IKnowledgeThemeService? _themeService;

    public SummariesController(
        ISummariesRetrievalService summariesRetrieval,
        ISummariesStatusService summariesStatus,
        IKnowledgeThemeService? themeService = null)
    {
        _summariesRetrieval = summariesRetrieval ?? throw new ArgumentNullException(nameof(summariesRetrieval));
        _summariesStatus = summariesStatus ?? throw new ArgumentNullException(nameof(summariesStatus));
        _themeService = themeService;
    }

    [HttpGet("retrieve")]
    [ProducesResponseType(typeof(IReadOnlyList<SearchResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Retrieve(
        [FromQuery] string query,
        [FromQuery] int topK = 5,
        [FromQuery] int maxExpanded = 10,
        [FromQuery] string? themes = null,
        CancellationToken cancellationToken = default)
    {
        var sourceIds = await ResolveThemeSourceIdsAsync(themes, cancellationToken).ConfigureAwait(false);
        if (sourceIds.IsFailure)
        {
            return BadRequest(new { error = sourceIds.Error });
        }

        var result = await _summariesRetrieval
            .RetrieveAsync(query, topK, maxExpanded, cancellationToken, sourceIds.Value)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpGet("status")]
    [Authorize(Roles = RoleDefinitions.Administrator)]
    [ProducesResponseType(typeof(SummariesStatusSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Status(CancellationToken cancellationToken = default)
    {
        var result = await _summariesStatus.GetAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    // Sprint 64 pattern: optional shared theme scope (comma-separated theme names) resolved to
    // source ids, following the RagsController/GraphRagController pattern. Null when no themes.
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
