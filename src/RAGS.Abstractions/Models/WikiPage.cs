using System.Text.Json.Serialization;

namespace Aletheia.RAGS.Abstractions.Models;

public sealed class WikiPage
{
    [JsonConstructor]
    public WikiPage()
    {
    }

    public WikiPage(
        Guid id,
        string topic,
        string title,
        string summary,
        IReadOnlyList<Guid>? sourceIds = null,
        IReadOnlyList<string>? citations = null,
        string generatedFrom = "wrags",
        int version = 1,
        string status = "Generated",
        float score = 0,
        int rank = 0,
        string retrievalStrategy = "wrags",
        Guid? primarySourceId = null,
        int? chunkIndex = null,
        IReadOnlyList<string>? relatedTopics = null,
        string? reviewedBy = null,
        DateTimeOffset? reviewedAt = null,
        bool isStale = false,
        string? staleReason = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Wiki page ID is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Topic is required.", nameof(topic));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        Id = id;
        Topic = topic.Trim();
        Title = title.Trim();
        Summary = summary?.Trim() ?? string.Empty;
        SourceIds = sourceIds ?? Array.Empty<Guid>();
        Citations = citations ?? Array.Empty<string>();
        GeneratedFrom = string.IsNullOrWhiteSpace(generatedFrom) ? "wrags" : generatedFrom.Trim();
        Version = Math.Max(1, version);
        Status = string.IsNullOrWhiteSpace(status) ? "Generated" : status.Trim();
        Score = score;
        Rank = rank;
        RetrievalStrategy = string.IsNullOrWhiteSpace(retrievalStrategy) ? "wrags" : retrievalStrategy.Trim();
        PrimarySourceId = primarySourceId;
        ChunkIndex = chunkIndex;
        RelatedTopics = relatedTopics ?? Array.Empty<string>();
        ReviewedBy = string.IsNullOrWhiteSpace(reviewedBy) ? null : reviewedBy.Trim();
        ReviewedAt = reviewedAt;
        IsStale = isStale;
        StaleReason = string.IsNullOrWhiteSpace(staleReason) ? null : staleReason.Trim();
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        UpdatedAt = updatedAt ?? CreatedAt;
    }

    public Guid Id { get; init; }

    public string Topic { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<Guid> SourceIds { get; init; } = Array.Empty<Guid>();

    public IReadOnlyList<string> Citations { get; init; } = Array.Empty<string>();

    public string GeneratedFrom { get; init; } = "wrags";

    public int Version { get; init; } = 1;

    public string Status { get; init; } = "Generated";

    public float Score { get; init; }

    public int Rank { get; init; }

    public string RetrievalStrategy { get; init; } = "wrags";

    public Guid? PrimarySourceId { get; init; }

    public int? ChunkIndex { get; init; }

    public IReadOnlyList<string> RelatedTopics { get; init; } = Array.Empty<string>();

    public string? ReviewedBy { get; init; }

    public DateTimeOffset? ReviewedAt { get; init; }

    public bool IsStale { get; init; }

    public string? StaleReason { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
