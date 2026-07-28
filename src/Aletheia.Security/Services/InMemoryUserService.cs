using Aletheia.Foundation.Security;
using Aletheia.Foundation.Shared;
using Aletheia.Security.Authentication;
using Microsoft.Extensions.Logging;

namespace Aletheia.Security.Services;

public sealed class InMemoryUserService : IUserService
{
    private readonly IUserStore _userStore;
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly ILogger<InMemoryUserService> _logger;

    public InMemoryUserService(IUserStore userStore, IRefreshTokenStore refreshTokenStore, ILogger<InMemoryUserService> logger)
    {
        _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
        _refreshTokenStore = refreshTokenStore ?? throw new ArgumentNullException(nameof(refreshTokenStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<UserIdentity>> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userStore.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return Result<UserIdentity>.Failure("User not found.");
        }

        var identity = ToIdentity(user);
        return Result<UserIdentity>.Success(identity);
    }

    public async Task<Result<UserIdentity>> GetUserByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = await _userStore.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return Result<UserIdentity>.Failure("User not found.");
        }

        var identity = ToIdentity(user);
        return Result<UserIdentity>.Success(identity);
    }

    public async Task<Result<IReadOnlyCollection<UserIdentity>>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = (await _userStore.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Select(ToIdentity)
            .ToList()
            .AsReadOnly();
        return Result<IReadOnlyCollection<UserIdentity>>.Success(users);
    }

    public async Task<Result<UserIdentity>> CreateUserAsync(string username, string email, string displayName, string password, IEnumerable<string>? roles = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return Result<UserIdentity>.Failure("Username is required.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return Result<UserIdentity>.Failure("Password is required.");
        }

        if (await _userStore.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false) != null)
        {
            return Result<UserIdentity>.Failure("Username already exists.");
        }

        var salt = LocalIdentityProvider.GenerateSalt();
        var hash = LocalIdentityProvider.HashPassword(password, salt);
        var userId = Guid.NewGuid().ToString("N");

        var user = new UserRecord(userId, username.Trim(), email.Trim(), displayName.Trim(), hash, salt, roles);
        await _userStore.AddAsync(user, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("User created: {Username} ({UserId}).", username, userId);
        return Result<UserIdentity>.Success(ToIdentity(user));
    }

    public async Task<Result> DisableUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userStore.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return Result.Failure("User not found.");
        }

        user.IsEnabled = false;
        await _userStore.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        await _refreshTokenStore.RevokeAllForUserAsync(userId, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("User disabled: {UserId}.", userId);
        return Result.Success();
    }

    public async Task<Result> EnableUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userStore.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return Result.Failure("User not found.");
        }

        user.IsEnabled = true;
        await _userStore.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("User enabled: {UserId}.", userId);
        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return Result.Failure("Password is required.");
        }

        var user = await _userStore.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return Result.Failure("User not found.");
        }

        var salt = LocalIdentityProvider.GenerateSalt();
        user.PasswordHash = LocalIdentityProvider.HashPassword(newPassword, salt);
        user.PasswordSalt = salt;
        await _userStore.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        await _refreshTokenStore.RevokeAllForUserAsync(userId, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Password reset for user: {UserId}.", userId);
        return Result.Success();
    }

    public async Task<Result> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userStore.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return Result.Failure("User not found.");
        }

        await _refreshTokenStore.RevokeAllForUserAsync(userId, cancellationToken).ConfigureAwait(false);
        await _userStore.RemoveAsync(userId, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("User deleted: {UserId}.", userId);
        return Result.Success();
    }

    public async Task<bool> IsUserEnabledAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userStore.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        return user?.IsEnabled ?? false;
    }

    private static UserIdentity ToIdentity(UserRecord user)
    {
        return new UserIdentity(
            user.UserId,
            user.Username,
            user.Email,
            user.DisplayName,
            user.Roles,
            "Local");
    }
}
