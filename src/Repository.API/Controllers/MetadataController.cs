using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Domain.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MetadataController : ControllerBase
{
    private readonly IMetadataUseCase _metadataUseCase;

    public MetadataController(IMetadataUseCase metadataUseCase)
    {
        _metadataUseCase = metadataUseCase ?? throw new ArgumentNullException(nameof(metadataUseCase));
    }

    [HttpGet]
    [ProducesResponseType(typeof(FileMetadata), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        [FromQuery] Guid fileId,
        [FromQuery] string fileName,
        [FromQuery] string? version,
        CancellationToken cancellationToken)
    {
        var descriptor = new FileDescriptor(fileId, fileName, version);
        var result = await _metadataUseCase.GetAsync(descriptor, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return NotFound(new { error = result.Error });
        }

        return Ok(result.Value);
    }
}
