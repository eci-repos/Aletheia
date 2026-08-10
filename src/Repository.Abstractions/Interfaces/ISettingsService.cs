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
}
