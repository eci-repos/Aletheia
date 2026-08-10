using System.Security.Claims;
using Aletheia.Foundation.Security;
using Aletheia.Repository.Abstractions.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Repository.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settings;

    public SettingsController(ISettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    // Global (admin-managed) settings
    [HttpGet]
    [Authorize(Roles = RoleDefinitions.Administrator)]
    public async Task<ActionResult<IReadOnlyDictionary<string, string>>> GetAppSettings(CancellationToken cancellationToken)
    {
        var result = await _settings.GetAppSettingsAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPut]
    [Authorize(Roles = RoleDefinitions.Administrator)]
    public async Task<IActionResult> UpdateAppSettings([FromBody] Dictionary<string, string> settings, CancellationToken cancellationToken)
    {
        if (settings is null || settings.Count == 0)
        {
            return BadRequest(new { error = "Settings payload is required." });
        }

        foreach (var kvp in settings)
        {
            var result = await _settings.SetAppSettingAsync(kvp.Key, kvp.Value, CurrentUserId, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return BadRequest(new { error = result.Error });
            }
        }

        var updated = await _settings.GetAppSettingsAsync(cancellationToken).ConfigureAwait(false);
        return updated.IsSuccess ? Ok(updated.Value) : StatusCode(500, new { error = updated.Error });
    }

    // Per-user settings
    [HttpGet("me")]
    public async Task<ActionResult<IReadOnlyDictionary<string, string>>> GetMySettings(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { error = "User identity is required." });
        }

        var result = await _settings.GetUserSettingsAsync(userId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMySettings([FromBody] Dictionary<string, string> settings, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { error = "User identity is required." });
        }

        if (settings is null || settings.Count == 0)
        {
            return BadRequest(new { error = "Settings payload is required." });
        }

        foreach (var kvp in settings)
        {
            var result = await _settings.SetUserSettingAsync(userId, kvp.Key, kvp.Value, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return BadRequest(new { error = result.Error });
            }
        }

        var updated = await _settings.GetUserSettingsAsync(userId, cancellationToken).ConfigureAwait(false);
        return updated.IsSuccess ? Ok(updated.Value) : StatusCode(500, new { error = updated.Error });
    }

    private string? CurrentUserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
