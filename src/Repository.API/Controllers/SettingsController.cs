using System.Security.Claims;
using Aletheia.Foundation.Security;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Repository.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SettingsController : ControllerBase
{
    private const int MaxAgentInstructionLength = 20_000;

    private readonly ISettingsService _settings;
    private readonly IAgentInstructionResolver? _agentInstructions;

    public SettingsController(ISettingsService settings, IAgentInstructionResolver? agentInstructions = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _agentInstructions = agentInstructions;
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

    // Agent instructions (Sprint 77) — per-role AI agent system prompts. Config-seeded baseline,
    // admin-overridable via app_settings; row-existence is the "modified" marker.
    [HttpGet("agent-instructions")]
    [Authorize(Roles = RoleDefinitions.Administrator)]
    public async Task<ActionResult<IReadOnlyList<Aletheia.RAGS.Abstractions.Models.AgentInstructionResolution>>> GetAgentInstructions(CancellationToken cancellationToken)
    {
        if (_agentInstructions is null)
        {
            return StatusCode(500, new { error = "Agent instruction resolver is not configured." });
        }

        var result = new List<Aletheia.RAGS.Abstractions.Models.AgentInstructionResolution>();
        foreach (var role in AgentInstructionRoles.All)
        {
            var resolved = await _agentInstructions.ResolveAsync(role, cancellationToken).ConfigureAwait(false);
            if (resolved.IsFailure)
            {
                return StatusCode(500, new { error = resolved.Error });
            }

            result.Add(resolved.Value);
        }

        return Ok(result);
    }

    [HttpPut("agent-instructions/{role}")]
    [Authorize(Roles = RoleDefinitions.Administrator)]
    public async Task<IActionResult> UpdateAgentInstruction(string role, [FromBody] UpdateAgentInstructionRequest request, CancellationToken cancellationToken)
    {
        if (!AgentInstructionRoles.IsKnown(role))
        {
            return BadRequest(new { error = $"Unknown agent instruction role '{role}'." });
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Value))
        {
            return BadRequest(new { error = "Agent instruction value is required." });
        }

        if (request.Value.Length > MaxAgentInstructionLength)
        {
            return BadRequest(new { error = $"Agent instruction exceeds the {MaxAgentInstructionLength} character limit." });
        }

        var result = await _settings.SetAppSettingAsync(
            AgentInstructionRoles.SettingKey(role),
            request.Value,
            CurrentUserId,
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Ok() : BadRequest(new { error = result.Error });
    }

    [HttpDelete("agent-instructions/{role}")]
    [Authorize(Roles = RoleDefinitions.Administrator)]
    public async Task<IActionResult> ResetAgentInstruction(string role, CancellationToken cancellationToken)
    {
        if (!AgentInstructionRoles.IsKnown(role))
        {
            return BadRequest(new { error = $"Unknown agent instruction role '{role}'." });
        }

        var result = await _settings.ClearAppSettingAsync(AgentInstructionRoles.SettingKey(role), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? NoContent() : BadRequest(new { error = result.Error });
    }

    private string? CurrentUserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}

public sealed record UpdateAgentInstructionRequest(string Value);
