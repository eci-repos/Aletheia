using Aletheia.Foundation.Shared;

namespace Aletheia.Foundation.Security;

public interface IUserService
{
    Task<Result<UserIdentity>> GetUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<Result<UserIdentity>> GetUserByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyCollection<UserIdentity>>> GetAllUsersAsync(CancellationToken cancellationToken = default);

    Task<Result<UserIdentity>> CreateUserAsync(string username, string email, string displayName, string password, IEnumerable<string>? roles = null, CancellationToken cancellationToken = default);

    Task<Result> DisableUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<Result> EnableUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<Result> ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default);

    Task<Result> DeleteUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<bool> IsUserEnabledAsync(string userId, CancellationToken cancellationToken = default);
}
