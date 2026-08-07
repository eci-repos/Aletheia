using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Repository.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class KnowledgeController : ControllerBase
{
    private readonly IKnowledgeThemeService _themeService;
    private readonly IMetadataRepository? _metadataRepository;
    private readonly TemplateReevaluationService? _reevaluationService;

    public KnowledgeController(
        IKnowledgeThemeService themeService,
        IMetadataRepository? metadataRepository = null,
        TemplateReevaluationService? reevaluationService = null)
    {
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _metadataRepository = metadataRepository;
        _reevaluationService = reevaluationService;
    }

    /// <summary>Sprint 58: knowledge themes with registered-document counts for the session theme picker.</summary>
    [HttpGet("themes")]
    public async Task<ActionResult<IReadOnlyList<Aletheia.RAGS.Abstractions.Models.KnowledgeThemeCount>>> GetThemes(CancellationToken cancellationToken)
    {
        var result = await _themeService
            .GetThemesWithCountsAsync(cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    /// <summary>Sprint 59: documents that are not Canonical (null or Uncategorized template status) for the admin list.</summary>
    [HttpGet("uncategorized")]
    public async Task<ActionResult<IReadOnlyList<FileThemeRow>>> GetUncategorized(CancellationToken cancellationToken)
    {
        if (_metadataRepository is null)
        {
            return StatusCode(500, new { error = "Metadata repository is not available." });
        }

        var result = await _metadataRepository
            .ListUncategorizedAsync(cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    /// <summary>Sprint 59: re-resolve the canonical template for one document (sourceId) or all non-Canonical documents, and generate briefs for promoted ones.</summary>
    [HttpPost("reevaluate")]
    public async Task<ActionResult<TemplateReevaluationSummary>> Reevaluate(
        [FromBody] TemplateReevaluationRequest? request,
        CancellationToken cancellationToken)
    {
        if (_reevaluationService is null)
        {
            return StatusCode(500, new { error = "Template re-evaluation is not available." });
        }

        var result = await _reevaluationService
            .ReevaluateAsync(request?.SourceId, cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }
}

/// <summary>Request body for POST /api/knowledge/reevaluate. Empty body re-evaluates all non-Canonical documents.</summary>
public sealed record TemplateReevaluationRequest(Guid? SourceId = null);
