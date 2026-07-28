using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Domain.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class VersionsController : ControllerBase
{
    private readonly IVersioningUseCase _versioningUseCase;

    public VersionsController(IVersioningUseCase versioningUseCase)
    {
        _versioningUseCase = versioningUseCase ?? throw new ArgumentNullException(nameof(versioningUseCase));
    }

    [HttpPost("create")]
    [ProducesResponseType(typeof(FileDescriptor), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateVersion(
        [FromBody] FileDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var result = await _versioningUseCase.CreateVersionAsync(descriptor, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<FileDescriptor>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListVersions(
        [FromQuery] Guid fileId,
        [FromQuery] string fileName,
        [FromQuery] string? version,
        CancellationToken cancellationToken)
    {
        var descriptor = new FileDescriptor(fileId, fileName, version);
        var result = await _versioningUseCase.ListVersionsAsync(descriptor, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }
}
