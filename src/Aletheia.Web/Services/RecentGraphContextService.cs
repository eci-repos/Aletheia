using System.Text.Json;
using Microsoft.JSInterop;

namespace Aletheia.Web.Services;

public sealed class RecentGraphContextService
{
    private const string StorageKey = "aletheia.graph.recentContext.v1";
    private const int LimitPerKind = 10;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IJSRuntime _jsRuntime;

    public RecentGraphContextService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime ?? throw new ArgumentNullException(nameof(jsRuntime));
    }

    public async Task<IReadOnlyList<RecentGraphContextItem>> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", cancellationToken, StorageKey).ConfigureAwait(false);
            return Deserialize(json);
        }
        catch
        {
            return Array.Empty<RecentGraphContextItem>();
        }
    }

    public async Task<IReadOnlyList<RecentGraphContextItem>> RecordDocumentAsync(
        Guid sourceId,
        string sourceName,
        CancellationToken cancellationToken = default)
    {
        var item = RecentGraphContextItem.Document(sourceId, sourceName, DateTimeOffset.UtcNow);
        return await UpsertAsync(item, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RecentGraphContextItem>> RecordSearchAsync(
        string query,
        string mode,
        CancellationToken cancellationToken = default)
    {
        var item = RecentGraphContextItem.Search(query, mode, DateTimeOffset.UtcNow);
        return await UpsertAsync(item, cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", cancellationToken, StorageKey).ConfigureAwait(false);
        }
        catch
        {
            // Recent context is a convenience; clearing is best-effort.
        }
    }

    private async Task<IReadOnlyList<RecentGraphContextItem>> UpsertAsync(
        RecentGraphContextItem item,
        CancellationToken cancellationToken)
    {
        var current = await GetAsync(cancellationToken).ConfigureAwait(false);
        var updated = Upsert(current, item);
        try
        {
            await _jsRuntime.InvokeVoidAsync(
                "localStorage.setItem",
                cancellationToken,
                StorageKey,
                JsonSerializer.Serialize(updated, JsonOptions)).ConfigureAwait(false);
        }
        catch
        {
            // Recent context is a convenience. Do not fail uploads/searches if browser storage is unavailable.
        }

        return updated;
    }

    public static IReadOnlyList<RecentGraphContextItem> Upsert(
        IReadOnlyList<RecentGraphContextItem> current,
        RecentGraphContextItem item)
    {
        var normalized = (current ?? Array.Empty<RecentGraphContextItem>())
            .Where(existing => !string.Equals(existing.Key, item.Key, StringComparison.OrdinalIgnoreCase))
            .Prepend(item)
            .GroupBy(existing => existing.Kind, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group
                .OrderByDescending(existing => existing.Timestamp)
                .Take(LimitPerKind))
            .OrderByDescending(existing => existing.Timestamp)
            .ToList();

        return normalized;
    }

    public static IReadOnlyList<RecentGraphContextItem> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<RecentGraphContextItem>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<RecentGraphContextItem>>(json, JsonOptions) is { } items
                ? items
                : Array.Empty<RecentGraphContextItem>();
        }
        catch
        {
            return Array.Empty<RecentGraphContextItem>();
        }
    }
}

public sealed record RecentGraphContextItem(
    string Key,
    string Kind,
    string Label,
    string? SourceId,
    string? Query,
    string? Mode,
    DateTimeOffset Timestamp)
{
    public static RecentGraphContextItem Document(Guid sourceId, string sourceName, DateTimeOffset timestamp)
    {
        return new RecentGraphContextItem(
            $"document:{sourceId:N}",
            "document",
            string.IsNullOrWhiteSpace(sourceName) ? sourceId.ToString("N") : sourceName.Trim(),
            sourceId.ToString(),
            null,
            null,
            timestamp);
    }

    public static RecentGraphContextItem Search(string query, string mode, DateTimeOffset timestamp)
    {
        var normalizedQuery = string.IsNullOrWhiteSpace(query) ? "search" : query.Trim();
        var normalizedMode = string.IsNullOrWhiteSpace(mode) ? "semantic" : mode.Trim().ToLowerInvariant();
        return new RecentGraphContextItem(
            $"search:{normalizedMode}:{normalizedQuery.ToLowerInvariant()}",
            "search",
            normalizedQuery,
            null,
            normalizedQuery,
            normalizedMode,
            timestamp);
    }
}
