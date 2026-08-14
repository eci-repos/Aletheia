using Aletheia.Foundation.Audit;

namespace Aletheia.Repository.Abstractions.Models;

public sealed class FileMetadata
{
    public FileMetadata(
        FileDescriptor descriptor,
        string contentType,
        long sizeBytes,
        DateTimeOffset uploadedAt,
        IReadOnlyDictionary<string, string>? tags = null,
        AuditInfo? auditInfo = null,
        string? contentHash = null,
        string? templateName = null,
        IReadOnlyList<string>? theme = null,
        string? templateStatus = null)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("Content type is required.", nameof(contentType));
        }

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "Size must be zero or greater.");
        }

        ContentType = contentType;
        SizeBytes = sizeBytes;
        UploadedAt = uploadedAt;
        Tags = tags is null ? new Dictionary<string, string>() : new Dictionary<string, string>(tags);
        AuditInfo = auditInfo;
        ContentHash = string.IsNullOrWhiteSpace(contentHash) ? null : contentHash;
        TemplateName = string.IsNullOrWhiteSpace(templateName) ? null : templateName;
        Theme = NormalizeThemes(theme);
        TemplateStatus = string.IsNullOrWhiteSpace(templateStatus) ? null : templateStatus;
    }

    public FileDescriptor Descriptor { get; }

    public string ContentType { get; }

    public long SizeBytes { get; }

    public DateTimeOffset UploadedAt { get; }

    public IReadOnlyDictionary<string, string> Tags { get; }

    public AuditInfo? AuditInfo { get; }

    /// <summary>SHA-256 (hex) of the uploaded content, when fingerprinting is enabled. Null for pre-existing rows.</summary>
    public string? ContentHash { get; }

    /// <summary>Canonical template name resolved at ingestion (docs/doc-templates). Null for pre-Sprint-58 rows.</summary>
    public string? TemplateName { get; }

    /// <summary>Knowledge themes of the canonical template (e.g. Analysis, As-Built, As-Proposed). Null for pre-Sprint-58 rows.</summary>
    public IReadOnlyList<string>? Theme { get; }

    /// <summary>Canonical template status (Canonical / Uncategorized). Null for pre-Sprint-59 rows awaiting re-evaluation.</summary>
    public string? TemplateStatus { get; }

    /// <summary>Number of RAGS embeddings for this source, populated by the search path (Sprint 69). Null when not populated.</summary>
    public int? ChunkCount { get; set; }

    /// <summary>True when the source has at least one embedding — i.e. ingestion completed and the document is retrievable.</summary>
    public bool Ingested => ChunkCount is > 0;

    /// <summary>True while an active ingestion job is still producing embeddings for this source
    /// (Sprint 69 post-sprint — the Repository Browser "Processing" state, so a mid-ingestion file
    /// with partial chunks is not shown as a premature "Ingested"). Populated by the search path.</summary>
    public bool IsProcessing { get; set; }

    private static IReadOnlyList<string>? NormalizeThemes(IReadOnlyList<string>? theme)
    {
        if (theme is null || theme.Count == 0)
        {
            return null;
        }

        var normalized = theme
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return normalized.Count == 0 ? null : normalized;
    }
}