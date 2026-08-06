using Aletheia.RAGS.Abstractions.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Repository.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class KnowledgeController : ControllerBase
{
    private readonly IKnowledgeThemeService _themeService;

    public KnowledgeController(IKnowledgeThemeService themeService)
    {
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
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
}