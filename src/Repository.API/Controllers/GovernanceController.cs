using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Repository.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GovernanceController : ControllerBase
{
    private readonly IGovernanceService _governance;

    public GovernanceController(IGovernanceService governance)
    {
        _governance = governance ?? throw new ArgumentNullException(nameof(governance));
    }

    // Roles
    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyList<Role>>> GetRoles(CancellationToken cancellationToken)
    {
        var result = await _governance.GetRolesAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("roles")]
    public async Task<ActionResult<Role>> CreateRole([FromBody] Role role, CancellationToken cancellationToken)
    {
        if (role is null) return BadRequest(new { error = "Role is required." });
        var result = await _governance.CreateRoleAsync(role, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpDelete("roles/{roleId}")]
    public async Task<ActionResult<bool>> DeleteRole(string roleId, CancellationToken cancellationToken)
    {
        var result = await _governance.DeleteRoleAsync(roleId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    // Permissions
    [HttpGet("roles/{roleId}/permissions")]
    public async Task<ActionResult<IReadOnlyList<Permission>>> GetPermissions(string roleId, CancellationToken cancellationToken)
    {
        var result = await _governance.GetPermissionsAsync(roleId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("roles/{roleId}/permissions/{permissionId}")]
    public async Task<ActionResult<bool>> AssignPermission(string roleId, string permissionId, CancellationToken cancellationToken)
    {
        var result = await _governance.AssignPermissionAsync(roleId, permissionId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpDelete("roles/{roleId}/permissions/{permissionId}")]
    public async Task<ActionResult<bool>> RevokePermission(string roleId, string permissionId, CancellationToken cancellationToken)
    {
        var result = await _governance.RevokePermissionAsync(roleId, permissionId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    // Audit Logs
    [HttpGet("audit-logs")]
    public async Task<ActionResult<IReadOnlyList<AuditLog>>> GetAuditLogs(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var result = await _governance.GetAuditLogsAsync(from, to, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("audit-log")]
    public async Task<ActionResult<bool>> LogAction([FromBody] LogActionPayload payload, CancellationToken cancellationToken)
    {
        if (payload is null) return BadRequest(new { error = "Payload is required." });
        var result = await _governance.LogActionAsync(payload.Action, payload.UserId, payload.ResourceId, payload.Details, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    // Retention Policies
    [HttpGet("retention-policies")]
    public async Task<ActionResult<IReadOnlyList<RetentionPolicy>>> GetRetentionPolicies(CancellationToken cancellationToken)
    {
        var result = await _governance.GetRetentionPoliciesAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("retention-policies")]
    public async Task<ActionResult<RetentionPolicy>> CreateRetentionPolicy([FromBody] RetentionPolicy policy, CancellationToken cancellationToken)
    {
        if (policy is null) return BadRequest(new { error = "Policy is required." });
        var result = await _governance.CreateRetentionPolicyAsync(policy, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpDelete("retention-policies/{policyId}")]
    public async Task<ActionResult<bool>> DeleteRetentionPolicy(string policyId, CancellationToken cancellationToken)
    {
        var result = await _governance.DeleteRetentionPolicyAsync(policyId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("retention-policies/evaluate/{resourceId}")]
    public async Task<ActionResult<bool>> EvaluateRetention(string resourceId, CancellationToken cancellationToken)
    {
        var result = await _governance.EvaluateRetentionAsync(resourceId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    // Compliance Rules
    [HttpGet("compliance-rules")]
    public async Task<ActionResult<IReadOnlyList<ComplianceRule>>> GetComplianceRules(CancellationToken cancellationToken)
    {
        var result = await _governance.GetComplianceRulesAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("compliance-rules")]
    public async Task<ActionResult<ComplianceRule>> AddComplianceRule([FromBody] ComplianceRule rule, CancellationToken cancellationToken)
    {
        if (rule is null) return BadRequest(new { error = "Rule is required." });
        var result = await _governance.AddComplianceRuleAsync(rule, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("compliance-check/{resourceId}")]
    public async Task<ActionResult<bool>> RunComplianceCheck(string resourceId, CancellationToken cancellationToken)
    {
        var result = await _governance.RunComplianceCheckAsync(resourceId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    // PII Scan
    [HttpPost("scan-pii")]
    public async Task<ActionResult<PiiDetectionResult>> ScanPii([FromBody] ScanPayload payload, CancellationToken cancellationToken)
    {
        if (payload is null) return BadRequest(new { error = "Payload is required." });
        var result = await _governance.ScanPiiAsync(payload.Content, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    public class LogActionPayload
    {
        public string Action { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string ResourceId { get; set; } = string.Empty;
        public string? Details { get; set; }
    }

    public class ScanPayload
    {
        public string Content { get; set; } = string.Empty;
    }
}
