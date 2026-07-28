using Aletheia.RAGS.Abstractions.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers;

[ApiController]
[Route("api/ontology")]
[Authorize]
public class OntologyController : ControllerBase
{
    private readonly IOntologyProvider _ontologyProvider;

    public OntologyController(IOntologyProvider ontologyProvider)
    {
        _ontologyProvider = ontologyProvider ?? throw new ArgumentNullException(nameof(ontologyProvider));
    }

    [HttpGet("entities")]
    [ProducesResponseType(typeof(IReadOnlyCollection<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetEntities(CancellationToken cancellationToken)
    {
        var result = await _ontologyProvider.GetEntitiesAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpGet("entities/{entity}/relationships")]
    [ProducesResponseType(typeof(IReadOnlyDictionary<string, string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRelationships(string entity, CancellationToken cancellationToken)
    {
        var result = await _ontologyProvider.GetRelationshipsAsync(entity, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }
}
