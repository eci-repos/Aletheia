using Aletheia.RAGS.Abstractions.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers;

[ApiController]
[Route("api/taxonomy")]
[Authorize]
public class TaxonomyController : ControllerBase
{
    private readonly ITaxonomyProvider _taxonomyProvider;

    public TaxonomyController(ITaxonomyProvider taxonomyProvider)
    {
        _taxonomyProvider = taxonomyProvider ?? throw new ArgumentNullException(nameof(taxonomyProvider));
    }

    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyCollection<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
    {
        var result = await _taxonomyProvider.GetCategoriesAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpGet("categories/{category}/tags")]
    [ProducesResponseType(typeof(IReadOnlyCollection<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTags(string category, CancellationToken cancellationToken)
    {
        var result = await _taxonomyProvider.GetTagsAsync(category, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }
}
