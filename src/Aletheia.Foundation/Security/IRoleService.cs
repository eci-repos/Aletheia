using Aletheia.Foundation.Shared;

namespace Aletheia.Foundation.Security;

public interface IRoleService
{
    Task<Result<IReadOnlyCollection<string>>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default);

    Task<Result> AssignRoleAsync(string userId, string role, CancellationToken cancellationToken = default);

    Task<Result> RemoveRoleAsync(string userId, string role, CancellationToken cancellationToken = default);

    Task<Result> ReplaceRolesAsync(string userId, IEnumerable<string> roles, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyCollection<string>>> GetAvailableRolesAsync(CancellationToken cancellationToken = default);
}
