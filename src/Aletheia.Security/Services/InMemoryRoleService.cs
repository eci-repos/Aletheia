using Aletheia.Foundation.Security;
using Aletheia.Foundation.Shared;
using Microsoft.Extensions.Logging;

namespace Aletheia.Security.Services;

public sealed class InMemoryRoleService : IRoleService
{
    private readonly IUserStore _userStore;
    private readonly ILogger<InMemoryRoleService> _logger;

    public InMemoryRoleService(IUserStore userStore, ILogger<InMemoryRoleService> logger)
    {
        _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<IReadOnlyCollection<string>>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userStore.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return Result<IReadOnlyCollection<string>>.Failure("User not found.");
        }

        return Result<IReadOnlyCollection<string>>.Success(user.Roles.ToList().AsReadOnly());
    }

    public async Task<Result> AssignRoleAsync(string userId, string role, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return Result.Failure("Role is required.");
        }

        var user = await _userStore.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return Result.Failure("User not found.");
        }

        if (!RoleDefinitions.AllRoles.Contains(role))
        {
            return Result.Failure($"Role '{role}' is not a recognized role.");
        }

        if (user.Roles.Contains(role))
        {
            return Result.Success();
        }

        user.Roles.Add(role);
        await _userStore.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Role '{Role}' assigned to user {UserId}.", role, userId);
        return Result.Success();
    }

    public async Task<Result> RemoveRoleAsync(string userId, string role, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return Result.Failure("Role is required.");
        }

        var user = await _userStore.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return Result.Failure("User not found.");
        }

        if (!user.Roles.Remove(role))
        {
            return Result.Failure($"User does not have role '{role}'.");
        }

        await _userStore.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Role '{Role}' removed from user {UserId}.", role, userId);
        return Result.Success();
    }

    public async Task<Result> ReplaceRolesAsync(string userId, IEnumerable<string> roles, CancellationToken cancellationToken = default)
    {
        var user = await _userStore.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return Result.Failure("User not found.");
        }

        var newRoles = roles?.Where(r => !string.IsNullOrWhiteSpace(r) && RoleDefinitions.AllRoles.Contains(r)).Distinct().ToList() ?? new List<string>();
        user.Roles.Clear();
        user.Roles.AddRange(newRoles);
        await _userStore.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Roles replaced for user {UserId}: {Roles}.", userId, string.Join(", ", newRoles));
        return Result.Success();
    }

    public Task<Result<IReadOnlyCollection<string>>> GetAvailableRolesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<IReadOnlyCollection<string>>.Success(RoleDefinitions.AllRoles.ToList().AsReadOnly()));
    }
}
