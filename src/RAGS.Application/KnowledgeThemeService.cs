using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aletheia.RAGS.Application;

/// <summary>
/// Sprint 58 knowledge-theme resolution: maps the user-selected session themes to the registered
/// sources that fall under them, and produces the theme catalog (theme + document count) for the UI.
/// Themes are read from file_metadata (persisted at ingestion) with a read-time fallback that
/// derives the theme from the file name via the canonical template registry.
/// </summary>
public sealed class KnowledgeThemeService : IKnowledgeThemeService
{
    public const string Uncategorized = "Uncategorized";

    /// <summary>Template status for documents that matched a canonical template at ingestion.</summary>
    public const string Canonical = "Canonical";

    private readonly IMetadataRepository? _metadataRepository;
    private readonly IDocumentTemplateRegistry? _templateRegistry;
    private readonly ILogger<KnowledgeThemeService> _logger;

    public KnowledgeThemeService(
        IMetadataRepository? metadataRepository = null,
        IDocumentTemplateRegistry? templateRegistry = null,
        ILogger<KnowledgeThemeService>? logger = null)
    {
        _metadataRepository = metadataRepository;
        _templateRegistry = templateRegistry;
        _logger = logger ?? NullLogger<KnowledgeThemeService>.Instance;
    }

    public async Task<Result<IReadOnlyList<Guid>>> ResolveSourceIdsAsync(
        IReadOnlyList<string> themes,
        CancellationToken cancellationToken = default)
    {
        if (themes is null || themes.Count == 0)
        {
            return Result<IReadOnlyList<Guid>>.Success(Array.Empty<Guid>());
        }

        var rows = await LoadThemeRowsAsync(cancellationToken).ConfigureAwait(false);
        if (rows is null)
        {
            return Result<IReadOnlyList<Guid>>.Success(Array.Empty<Guid>());
        }

        var requested = new HashSet<string>(themes, StringComparer.OrdinalIgnoreCase);
        var sourceIds = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var row in rows)
        {
            // Multi-theme: a document matches when any of its themes is in the requested set.
            if (seen.Add(row.FileId) && ResolveThemes(row).Any(requested.Contains))
            {
                sourceIds.Add(row.FileId);
            }
        }

        _logger.LogInformation("Knowledge theme filter resolved {ThemeCount} theme(s) to {SourceCount} source(s).", requested.Count, sourceIds.Count);
        return Result<IReadOnlyList<Guid>>.Success(sourceIds);
    }

    public async Task<Result<IReadOnlyList<KnowledgeThemeCount>>> GetThemesWithCountsAsync(
        CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var rows = await LoadThemeRowsAsync(cancellationToken).ConfigureAwait(false);
        if (rows is not null)
        {
            var seenFiles = new HashSet<Guid>();
            foreach (var row in rows)
            {
                if (!seenFiles.Add(row.FileId))
                {
                    continue;
                }

                // Multi-theme: a document counts in each of its themes.
                foreach (var theme in ResolveThemes(row))
                {
                    counts[theme] = counts.GetValueOrDefault(theme) + 1;
                }
            }
        }

        var ordered = new List<KnowledgeThemeCount>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Registry themes first (including themes with zero documents so the picker is stable).
        if (_templateRegistry is not null)
        {
            foreach (var theme in _templateRegistry.ListThemes())
            {
                ordered.Add(new KnowledgeThemeCount(theme, counts.GetValueOrDefault(theme)));
                added.Add(theme);
            }
        }

        // Any themes seen in metadata that are not declared by a template (e.g. Uncategorized fallback).
        foreach (var pair in counts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (added.Add(pair.Key))
            {
                ordered.Add(new KnowledgeThemeCount(pair.Key, pair.Value));
            }
        }

        return Result<IReadOnlyList<KnowledgeThemeCount>>.Success(ordered);
    }

    private async Task<IReadOnlyList<FileThemeRow>?> LoadThemeRowsAsync(CancellationToken cancellationToken)
    {
        if (_metadataRepository is null)
        {
            return null;
        }

        var result = await _metadataRepository
            .ListThemeRowsAsync(cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }

    private IReadOnlyList<string> ResolveThemes(FileThemeRow row)
    {
        if (row.Theme is { Count: > 0 })
        {
            return row.Theme;
        }

        // Read-time fallback (safety net): derive the theme set from the file name via the registry.
        var derived = _templateRegistry?.TryGetThemes(row.FileName);
        return derived is { Count: > 0 } ? derived : new List<string> { Uncategorized };
    }
}