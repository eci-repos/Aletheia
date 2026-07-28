using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Abstractions.Interfaces;

public interface IGovernanceService
{
    Task<Result<IReadOnlyList<Role>>> GetRolesAsync(CancellationToken cancellationToken = default);
    Task<Result<Role>> CreateRoleAsync(Role role, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteRoleAsync(string roleId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<Permission>>> GetPermissionsAsync(string roleId, CancellationToken cancellationToken = default);
    Task<Result<bool>> AssignPermissionAsync(string roleId, string permissionId, CancellationToken cancellationToken = default);
    Task<Result<bool>> RevokePermissionAsync(string roleId, string permissionId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<AuditLog>>> GetAuditLogsAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default);
    Task<Result<bool>> LogActionAsync(string action, string userId, string resourceId, string? details = null, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<RetentionPolicy>>> GetRetentionPoliciesAsync(CancellationToken cancellationToken = default);
    Task<Result<RetentionPolicy>> CreateRetentionPolicyAsync(RetentionPolicy policy, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteRetentionPolicyAsync(string policyId, CancellationToken cancellationToken = default);

    Task<Result<bool>> EvaluateRetentionAsync(string resourceId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ComplianceRule>>> GetComplianceRulesAsync(CancellationToken cancellationToken = default);
    Task<Result<ComplianceRule>> AddComplianceRuleAsync(ComplianceRule rule, CancellationToken cancellationToken = default);
    Task<Result<bool>> RunComplianceCheckAsync(string resourceId, CancellationToken cancellationToken = default);

    Task<Result<PiiDetectionResult>> ScanPiiAsync(string content, CancellationToken cancellationToken = default);
}
