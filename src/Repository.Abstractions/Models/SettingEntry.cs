namespace Aletheia.Repository.Abstractions.Models;

/// <summary>A single key/value setting entry (Sprint 61 settings foundation).</summary>
public sealed class SettingEntry
{
    public string Key { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? UpdatedBy { get; init; }
}
