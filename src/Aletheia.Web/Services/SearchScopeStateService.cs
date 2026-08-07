using System.Text.Json;
using Microsoft.JSInterop;

namespace Aletheia.Web.Services;

/// <summary>
/// Sprint 59: the shared knowledge-theme scope across surfaces (Phase 1). Holds the theme selection
/// that Search Center honors as an optional filter on semantic search. Empty = all themes (no scope).
/// Persisted in localStorage so the scope survives reloads; Copilot keeps its own session-scoped filter.
/// </summary>
public sealed class SearchScopeStateService
{
    private const string StorageKey = "aletheia.search.scope.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IJSRuntime _jsRuntime;
    private bool _restored;

    public SearchScopeStateService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime ?? throw new ArgumentNullException(nameof(jsRuntime));
    }

    public IReadOnlyList<string> SelectedThemes { get; private set; } = new List<string>();

    public async Task<IReadOnlyList<string>> RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (_restored)
        {
            return new List<string>(SelectedThemes);
        }

        try
        {
            var json = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", cancellationToken, StorageKey).ConfigureAwait(false);
            SelectedThemes = Deserialize(json);
        }
        catch
        {
            SelectedThemes = new List<string>();
        }

        _restored = true;
        return new List<string>(SelectedThemes);
    }

    public async Task SetThemesAsync(IReadOnlyList<string> themes, CancellationToken cancellationToken = default)
    {
        SelectedThemes = themes is { Count: > 0 } ? new List<string>(themes) : new List<string>();
        _restored = true;

        try
        {
            await _jsRuntime.InvokeVoidAsync(
                "localStorage.setItem",
                cancellationToken,
                StorageKey,
                JsonSerializer.Serialize(SelectedThemes, JsonOptions)).ConfigureAwait(false);
        }
        catch
        {
            // Search scope is convenience state; the in-memory value is already updated.
        }
    }

    private static IReadOnlyList<string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
