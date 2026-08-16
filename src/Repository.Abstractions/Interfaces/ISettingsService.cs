using Aletheia.Foundation.Shared;

namespace Aletheia.Repository.Abstractions.Interfaces;

/// <summary>Singleton settings service with typed accessors and caching (Sprint 61). A null
/// <paramref name="userId"/> targets the app/global scope; otherwise the user's own settings.</summary>
public interface ISettingsService
{
    // App (global, admin-managed) settings
    Task<Result<IReadOnlyDictionary<string, string>>> GetAppSettingsAsync(CancellationToken cancellationToken = default);
    Task<Result<bool>> SetAppSettingAsync(string key, string value, string? updatedBy = null, CancellationToken cancellationToken = default);

    // Per-user settings
    Task<Result<IReadOnlyDictionary<string, string>>> GetUserSettingsAsync(string userId, CancellationToken cancellationToken = default);
    Task<Result<bool>> SetUserSettingAsync(string userId, string key, string value, CancellationToken cancellationToken = default);

    // Typed accessors (userId null => app/global scope)
    Task<Result<bool>> GetBoolAsync(string key, bool defaultValue, string? userId = null, CancellationToken cancellationToken = default);
    Task<Result<bool>> SetBoolAsync(string key, bool value, string? userId = null, CancellationToken cancellationToken = default);

    // String accessors (Sprint 77) — free-text settings (agent instructions, etc.). GetStringAsync
    // returns null when the key has no stored value; SetStringAsync targets app scope when userId is null.
    Task<Result<string?>> GetStringAsync(string key, string? userId = null, CancellationToken cancellationToken = default);
    Task<Result<bool>> SetStringAsync(string key, string value, string? userId = null, CancellationToken cancellationToken = default);

    // App-scope delete (Sprint 77) — removes a row so a setting returns to its config default.
    Task<Result<bool>> ClearAppSettingAsync(string key, CancellationToken cancellationToken = default);
}
