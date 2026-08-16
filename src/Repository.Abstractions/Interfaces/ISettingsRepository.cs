using Aletheia.Foundation.Shared;

namespace Aletheia.Repository.Abstractions.Interfaces;

/// <summary>Raw persistence for the settings store (Sprint 61). App settings are global and
/// admin-managed; user settings are keyed by user id.</summary>
public interface ISettingsRepository
{
    Task<Result<IReadOnlyDictionary<string, string>>> GetAppSettingsAsync(CancellationToken cancellationToken = default);

    Task<Result<bool>> UpsertAppSettingAsync(string key, string value, string? updatedBy = null, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyDictionary<string, string>>> GetUserSettingsAsync(string userId, CancellationToken cancellationToken = default);

    Task<Result<bool>> UpsertUserSettingAsync(string userId, string key, string value, CancellationToken cancellationToken = default);

    // App-scope delete (Sprint 77) — removes a row so a setting returns to its config default.
    Task<Result<bool>> DeleteAppSettingAsync(string key, CancellationToken cancellationToken = default);
}
