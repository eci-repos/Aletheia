using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Application.UseCases.Governance;

public sealed class GovernanceService : IGovernanceService
{
    private readonly ConcurrentDictionary<string, Role> _roles = new();
    private readonly ConcurrentDictionary<string, Permission> _permissions = new();
    private readonly List<AuditLog> _auditLogs = new();
    private readonly object _auditLock = new();
    private readonly ConcurrentDictionary<string, RetentionPolicy> _policies = new();
    private readonly ConcurrentDictionary<string, ComplianceRule> _rules = new();

    public GovernanceService()
    {
        // Seed default roles and permissions
        var adminRole = new Role { Id = "admin", Name = "Administrator", Permissions = new List<string> { "read", "write", "delete", "manage_users" } };
        var readerRole = new Role { Id = "reader", Name = "Reader", Permissions = new List<string> { "read" } };
        _roles[adminRole.Id] = adminRole;
        _roles[readerRole.Id] = readerRole;
    }

    // Roles
    public Task<Result<IReadOnlyList<Role>>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = _roles.Values.ToList();
        return Task.FromResult(Result<IReadOnlyList<Role>>.Success(roles));
    }

    public Task<Result<Role>> CreateRoleAsync(Role role, CancellationToken cancellationToken = default)
    {
        if (role is null)
        {
            throw new ArgumentNullException(nameof(role));
        }

        if (string.IsNullOrEmpty(role.Id))
        {
            role.Id = Guid.NewGuid().ToString("N");
        }

        _roles[role.Id] = role;
        return Task.FromResult(Result<Role>.Success(role));
    }

    public Task<Result<bool>> DeleteRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var removed = _roles.TryRemove(roleId, out _);
        return Task.FromResult(Result<bool>.Success(removed));
    }

    // Permissions
    public Task<Result<IReadOnlyList<Permission>>> GetPermissionsAsync(string roleId, CancellationToken cancellationToken = default)
    {
        if (!_roles.TryGetValue(roleId, out var role))
        {
            return Task.FromResult(Result<IReadOnlyList<Permission>>.Failure("Role not found."));
        }

        var permissions = role.Permissions
            .Select(pid => _permissions.TryGetValue(pid, out var p) ? p : new Permission { Id = pid, Name = pid, Action = pid })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<Permission>>.Success(permissions));
    }

    public Task<Result<bool>> AssignPermissionAsync(string roleId, string permissionId, CancellationToken cancellationToken = default)
    {
        if (!_roles.TryGetValue(roleId, out var role))
        {
            return Task.FromResult(Result<bool>.Success(false));
        }

        if (!role.Permissions.Contains(permissionId))
        {
            role.Permissions.Add(permissionId);
        }

        return Task.FromResult(Result<bool>.Success(true));
    }

    public Task<Result<bool>> RevokePermissionAsync(string roleId, string permissionId, CancellationToken cancellationToken = default)
    {
        if (!_roles.TryGetValue(roleId, out var role))
        {
            return Task.FromResult(Result<bool>.Success(false));
        }

        role.Permissions.Remove(permissionId);
        return Task.FromResult(Result<bool>.Success(true));
    }

    // Audit
    public Task<Result<IReadOnlyList<AuditLog>>> GetAuditLogsAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        lock (_auditLock)
        {
            var logs = _auditLogs
                .Where(l => from is null || l.Timestamp >= from)
                .Where(l => to is null || l.Timestamp <= to)
                .OrderByDescending(l => l.Timestamp)
                .ToList();

            return Task.FromResult(Result<IReadOnlyList<AuditLog>>.Success(logs));
        }
    }

    public Task<Result<bool>> LogActionAsync(string action, string userId, string resourceId, string? details = null, CancellationToken cancellationToken = default)
    {
        var log = new AuditLog
        {
            Action = action,
            UserId = userId,
            ResourceId = resourceId,
            Details = details,
            Timestamp = DateTimeOffset.UtcNow
        };

        lock (_auditLock)
        {
            _auditLogs.Add(log);
        }

        return Task.FromResult(Result<bool>.Success(true));
    }

    // Retention
    public Task<Result<IReadOnlyList<RetentionPolicy>>> GetRetentionPoliciesAsync(CancellationToken cancellationToken = default)
    {
        var policies = _policies.Values.ToList();
        return Task.FromResult(Result<IReadOnlyList<RetentionPolicy>>.Success(policies));
    }

    public Task<Result<RetentionPolicy>> CreateRetentionPolicyAsync(RetentionPolicy policy, CancellationToken cancellationToken = default)
    {
        if (policy is null)
        {
            throw new ArgumentNullException(nameof(policy));
        }

        if (string.IsNullOrEmpty(policy.Id))
        {
            policy.Id = Guid.NewGuid().ToString("N");
        }

        _policies[policy.Id] = policy;
        return Task.FromResult(Result<RetentionPolicy>.Success(policy));
    }

    public Task<Result<bool>> DeleteRetentionPolicyAsync(string policyId, CancellationToken cancellationToken = default)
    {
        var removed = _policies.TryRemove(policyId, out _);
        return Task.FromResult(Result<bool>.Success(removed));
    }

    public Task<Result<bool>> EvaluateRetentionAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        // Dummy evaluation: always pass for this implementation
        return Task.FromResult(Result<bool>.Success(true));
    }

    // Compliance
    public Task<Result<IReadOnlyList<ComplianceRule>>> GetComplianceRulesAsync(CancellationToken cancellationToken = default)
    {
        var rules = _rules.Values.ToList();
        return Task.FromResult(Result<IReadOnlyList<ComplianceRule>>.Success(rules));
    }

    public Task<Result<ComplianceRule>> AddComplianceRuleAsync(ComplianceRule rule, CancellationToken cancellationToken = default)
    {
        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        if (string.IsNullOrEmpty(rule.Id))
        {
            rule.Id = Guid.NewGuid().ToString("N");
        }

        _rules[rule.Id] = rule;
        return Task.FromResult(Result<ComplianceRule>.Success(rule));
    }

    public Task<Result<bool>> RunComplianceCheckAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        var issues = new List<string>();
        foreach (var rule in _rules.Values)
        {
            issues.Add($"Rule '{rule.Name}' ({rule.RuleType})Severity: {rule.Severity}");
        }

        if (issues.Count == 0)
        {
            // Always pass
            return Task.FromResult(Result<bool>.Success(true));
        }

        return Task.FromResult(Result<bool>.Success(true));
    }

    // PII Scan
    public Task<Result<PiiDetectionResult>> ScanPiiAsync(string content, CancellationToken cancellationToken = default)
    {
        var result = new PiiDetectionResult();
        if (string.IsNullOrWhiteSpace(content))
        {
            return Task.FromResult(Result<PiiDetectionResult>.Success(result));
        }

        // Simple regex-based detection (mock)
        var patterns = new Dictionary<string, Regex>
        {
            ["email"] = new Regex(@"[\w.]+@[\w.]+\.[\w]+", RegexOptions.Compiled),
            ["phone"] = new Regex(@"\b\d{3}[-. ]?\d{3}[-. ]?\d{4}\b", RegexOptions.Compiled),
            ["ssn"] = new Regex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)
        };

        foreach (var kvp in patterns)
        {
            foreach (Match match in kvp.Value.Matches(content))
            {
                result.Matches.Add(new PiiMatch
                {
                    PiiType = kvp.Key,
                    MaskedValue = "***MASKED***",
                    StartIndex = match.Index,
                    EndIndex = match.Index + match.Length
                });
            }
        }

        result.PiiDetected = result.Matches.Count > 0;
        return Task.FromResult(Result<PiiDetectionResult>.Success(result));
    }
}
