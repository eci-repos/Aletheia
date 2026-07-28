using System.Collections.Concurrent;

namespace Aletheia.Security.Services;

public sealed class UserRecord
{
    public string UserId { get; }
    public string Username { get; }
    public string Email { get; }
    public string DisplayName { get; }
    public string PasswordHash { get; set; }
    public string PasswordSalt { get; set; }
    public List<string> Roles { get; } = new();
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; }

    public UserRecord(
        string userId,
        string username,
        string email,
        string displayName,
        string passwordHash,
        string passwordSalt,
        IEnumerable<string>? roles = null,
        bool isEnabled = true,
        DateTimeOffset? createdAt = null)
    {
        UserId = userId;
        Username = username;
        Email = email;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        PasswordSalt = passwordSalt;
        IsEnabled = isEnabled;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        if (roles != null)
        {
            Roles.AddRange(roles.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct());
        }
    }
}

public interface IUserStore
{
    Task AddAsync(UserRecord user, CancellationToken cancellationToken = default);

    Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default);

    Task<UserRecord?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);

    Task<UserRecord?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<UserRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryUserStore : IUserStore
{
    private readonly ConcurrentDictionary<string, UserRecord> _usersById = new();
    private readonly ConcurrentDictionary<string, UserRecord> _usersByUsername = new();

    public Task AddAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        Upsert(user);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        Upsert(user);
        return Task.CompletedTask;
    }

    public Task<UserRecord?> GetByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        _usersById.TryGetValue(userId, out var user);
        return Task.FromResult(user);
    }

    public Task<UserRecord?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        _usersByUsername.TryGetValue(username.ToLowerInvariant(), out var user);
        return Task.FromResult(user);
    }

    public Task<IReadOnlyCollection<UserRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyCollection<UserRecord>>(_usersById.Values.ToList().AsReadOnly());
    }

    public Task<bool> RemoveAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (_usersById.TryRemove(userId, out var user))
        {
            _usersByUsername.TryRemove(user.Username.ToLowerInvariant(), out _);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    private void Upsert(UserRecord user)
    {
        _usersById[user.UserId] = user;
        _usersByUsername[user.Username.ToLowerInvariant()] = user;
    }
}
