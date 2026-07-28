using System.Collections.Concurrent;
using Aletheia.Foundation.Security;

namespace Aletheia.Security.Authentication;

public interface IRefreshTokenStore
{
    Task AddAsync(RefreshTokenEntry entry, CancellationToken cancellationToken = default);

    Task<RefreshTokenEntry?> GetAsync(string token, CancellationToken cancellationToken = default);

    Task RevokeAsync(string token, CancellationToken cancellationToken = default);

    Task RevokeAllForUserAsync(string userId, CancellationToken cancellationToken = default);

    Task CleanupExpiredAsync(CancellationToken cancellationToken = default);
}

public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly ConcurrentDictionary<string, RefreshTokenEntry> _tokens = new();

    public Task AddAsync(RefreshTokenEntry entry, CancellationToken cancellationToken = default)
    {
        _tokens[entry.Token] = entry;
        return Task.CompletedTask;
    }

    public Task<RefreshTokenEntry?> GetAsync(string token, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_tokens.TryGetValue(token, out var entry) ? entry : null);
    }

    public Task RevokeAsync(string token, CancellationToken cancellationToken = default)
    {
        if (_tokens.TryGetValue(token, out var entry))
        {
            entry.IsRevoked = true;
        }

        return Task.CompletedTask;
    }

    public Task RevokeAllForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        foreach (var token in _tokens.Values.Where(t => t.UserId == userId))
        {
            token.IsRevoked = true;
        }

        return Task.CompletedTask;
    }

    public Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        var expired = _tokens.Where(kvp => kvp.Value.IsExpired).Select(kvp => kvp.Key).ToList();
        foreach (var key in expired)
        {
            _tokens.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }
}
