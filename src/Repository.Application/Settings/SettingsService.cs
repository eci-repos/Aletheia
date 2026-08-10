using System.Collections.Concurrent;
using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;

namespace Aletheia.Repository.Application.Settings;

/// <summary>Singleton settings service (Sprint 61). App settings are cached globally; user
/// settings are cached per user. Writes go through to the repository and update the cache, so
/// the cache never goes stale within a single API process.</summary>
public sealed class SettingsService : ISettingsService
{
    private readonly ISettingsRepository _repository;
    private readonly ConcurrentDictionary<string, string> _appCache = new();
    private readonly ConcurrentDictionary<string, UserSettingsCache> _userCaches = new();
    private readonly SemaphoreSlim _appLoadLock = new(1, 1);
    private bool _appLoaded;

    public SettingsService(ISettingsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<Result<IReadOnlyDictionary<string, string>>> GetAppSettingsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAppLoadedAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyDictionary<string, string>>.Success(new Dictionary<string, string>(_appCache));
    }

    public async Task<Result<bool>> SetAppSettingAsync(string key, string value, string? updatedBy = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result<bool>.Failure("Setting key is required.");
        }

        var result = await _repository.UpsertAppSettingAsync(key, value, updatedBy, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            _appCache[key] = value;
        }

        return result;
    }

    public async Task<Result<IReadOnlyDictionary<string, string>>> GetUserSettingsAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure("User id is required.");
        }

        var cache = await GetUserCacheAsync(userId, cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyDictionary<string, string>>.Success(new Dictionary<string, string>(cache.Values));
    }

    public async Task<Result<bool>> SetUserSettingAsync(string userId, string key, string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result<bool>.Failure("User id is required.");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return Result<bool>.Failure("Setting key is required.");
        }

        var result = await _repository.UpsertUserSettingAsync(userId, key, value, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            var cache = _userCaches.GetOrAdd(userId, _ => new UserSettingsCache());
            cache.Values[key] = value;
        }

        return result;
    }

    public async Task<Result<bool>> GetBoolAsync(string key, bool defaultValue, string? userId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result<bool>.Failure("Setting key is required.");
        }

        string? raw;
        if (userId is null)
        {
            await EnsureAppLoadedAsync(cancellationToken).ConfigureAwait(false);
            raw = _appCache.TryGetValue(key, out var value) ? value : null;
        }
        else
        {
            var cache = await GetUserCacheAsync(userId, cancellationToken).ConfigureAwait(false);
            raw = cache.Values.TryGetValue(key, out var value) ? value : null;
        }

        if (raw is null)
        {
            return Result<bool>.Success(defaultValue);
        }

        return Result<bool>.Success(bool.TryParse(raw, out var parsed) ? parsed : defaultValue);
    }

    public async Task<Result<bool>> SetBoolAsync(string key, bool value, string? userId = null, CancellationToken cancellationToken = default)
    {
        return userId is null
            ? await SetAppSettingAsync(key, value.ToString(), updatedBy: null, cancellationToken).ConfigureAwait(false)
            : await SetUserSettingAsync(userId, key, value.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureAppLoadedAsync(CancellationToken cancellationToken)
    {
        if (_appLoaded)
        {
            return;
        }

        await _appLoadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_appLoaded)
            {
                return;
            }

            var result = await _repository.GetAppSettingsAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess && result.Value is not null)
            {
                foreach (var kvp in result.Value)
                {
                    _appCache[kvp.Key] = kvp.Value;
                }

                _appLoaded = true;
            }
        }
        finally
        {
            _appLoadLock.Release();
        }
    }

    private async Task<UserSettingsCache> GetUserCacheAsync(string userId, CancellationToken cancellationToken)
    {
        var cache = _userCaches.GetOrAdd(userId, _ => new UserSettingsCache());
        if (cache.Loaded)
        {
            return cache;
        }

        await cache.LoadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cache.Loaded)
            {
                return cache;
            }

            var result = await _repository.GetUserSettingsAsync(userId, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess && result.Value is not null)
            {
                foreach (var kvp in result.Value)
                {
                    cache.Values[kvp.Key] = kvp.Value;
                }

                cache.Loaded = true;
            }
        }
        finally
        {
            cache.LoadLock.Release();
        }

        return cache;
    }

    private sealed class UserSettingsCache
    {
        public ConcurrentDictionary<string, string> Values { get; } = new();
        public SemaphoreSlim LoadLock { get; } = new(1, 1);
        public bool Loaded { get; set; }
    }
}
