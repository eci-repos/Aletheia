using System.Text.Json;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;
using Npgsql;

namespace Aletheia.RAGS.Infrastructure.PostgreSQL.Wiki;

public sealed class PostgreSqlWikiPageRepository : IWikiPageRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly PostgreSqlWikiSchema _schema;

    public PostgreSqlWikiPageRepository(
        PostgreSqlConnectionFactory connectionFactory,
        PostgreSqlWikiSchema schema)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
    }

    public async Task<Result<IReadOnlyList<WikiPage>>> SearchAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        await _schema.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
SELECT
    id AS Id,
    topic AS Topic,
    title AS Title,
    summary AS Summary,
    source_ids::text AS SourceIds,
    citations::text AS Citations,
    generated_from AS GeneratedFrom,
    version AS Version,
    status AS Status,
    score AS Score,
    rank AS Rank,
    retrieval_strategy AS RetrievalStrategy,
    primary_source_id AS PrimarySourceId,
    chunk_index AS ChunkIndex,
    related_topics::text AS RelatedTopics,
    reviewed_by AS ReviewedBy,
    reviewed_at AS ReviewedAt,
    (
        SELECT MAX(fm.uploaded_at)
        FROM file_metadata fm
        WHERE fm.file_id::text IN (SELECT jsonb_array_elements_text(source_ids))
    ) AS SourceChangedAt,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt
FROM wiki_pages
WHERE (topic ILIKE @Pattern
   OR title ILIKE @Pattern
   OR summary ILIKE @Pattern)
  AND generated_from <> 'graphrag'
ORDER BY
    CASE WHEN generated_from = 'document-brief' THEN 0 ELSE 1 END,
    CASE WHEN topic_normalized = @Normalized THEN 0 ELSE 1 END,
    updated_at DESC,
    score DESC
LIMIT @Take;";

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var rows = await connection.QueryAsync<WikiPageRow>(
                sql,
                new
                {
                    Pattern = $"%{EscapeLike(query.Trim())}%",
                    Normalized = Normalize(query),
                    Take = Math.Clamp(topK, 1, 50)
                }).ConfigureAwait(false);

            return Result<IReadOnlyList<WikiPage>>.Success(rows.Select(Map).ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<WikiPage>>.Failure($"WRAGS wiki search failed. {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<WikiPage>>> GetRecentAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        await _schema.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
SELECT
    id AS Id,
    topic AS Topic,
    title AS Title,
    summary AS Summary,
    source_ids::text AS SourceIds,
    citations::text AS Citations,
    generated_from AS GeneratedFrom,
    version AS Version,
    status AS Status,
    score AS Score,
    rank AS Rank,
    retrieval_strategy AS RetrievalStrategy,
    primary_source_id AS PrimarySourceId,
    chunk_index AS ChunkIndex,
    related_topics::text AS RelatedTopics,
    reviewed_by AS ReviewedBy,
    reviewed_at AS ReviewedAt,
    (
        SELECT MAX(fm.uploaded_at)
        FROM file_metadata fm
        WHERE fm.file_id::text IN (SELECT jsonb_array_elements_text(source_ids))
    ) AS SourceChangedAt,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt
FROM wiki_pages
WHERE generated_from <> 'graphrag'
ORDER BY
    CASE WHEN generated_from = 'document-brief' THEN 0 ELSE 1 END,
    updated_at DESC
LIMIT @Take;";

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var rows = await connection.QueryAsync<WikiPageRow>(
                sql,
                new { Take = Math.Clamp(take, 1, 50) }).ConfigureAwait(false);

            return Result<IReadOnlyList<WikiPage>>.Success(rows.Select(Map).ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<WikiPage>>.Failure($"WRAGS recent pages failed. {ex.Message}");
        }
    }

    public async Task<Result<WikiPage?>> GetAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        await _schema.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
SELECT
    id AS Id,
    topic AS Topic,
    title AS Title,
    summary AS Summary,
    source_ids::text AS SourceIds,
    citations::text AS Citations,
    generated_from AS GeneratedFrom,
    version AS Version,
    status AS Status,
    score AS Score,
    rank AS Rank,
    retrieval_strategy AS RetrievalStrategy,
    primary_source_id AS PrimarySourceId,
    chunk_index AS ChunkIndex,
    related_topics::text AS RelatedTopics,
    reviewed_by AS ReviewedBy,
    reviewed_at AS ReviewedAt,
    (
        SELECT MAX(fm.uploaded_at)
        FROM file_metadata fm
        WHERE fm.file_id::text IN (SELECT jsonb_array_elements_text(source_ids))
    ) AS SourceChangedAt,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt
FROM wiki_pages
WHERE id = @PageId;";

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var row = await connection.QuerySingleOrDefaultAsync<WikiPageRow>(
                sql,
                new { PageId = pageId }).ConfigureAwait(false);

            return Result<WikiPage?>.Success(row is null ? null : Map(row));
        }
        catch (Exception ex)
        {
            return Result<WikiPage?>.Failure($"WRAGS page lookup failed. {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<WikiPageLink>>> GetRelatedAsync(
        Guid pageId,
        int take,
        CancellationToken cancellationToken = default)
    {
        await _schema.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
WITH current_page AS (
    SELECT source_ids, related_topics, topic_normalized
    FROM wiki_pages
    WHERE id = @PageId
)
SELECT DISTINCT
    p.id AS Id,
    p.topic AS Topic,
    p.title AS Title,
    p.status AS Status,
    p.version AS Version,
    p.updated_at AS UpdatedAt
FROM wiki_pages p
CROSS JOIN current_page c
WHERE p.id <> @PageId
  AND (
      p.source_ids ?| ARRAY(SELECT jsonb_array_elements_text(c.source_ids))
      OR p.related_topics ?| ARRAY(SELECT jsonb_array_elements_text(c.related_topics))
      OR p.topic_normalized IN (
          SELECT lower(value)
          FROM jsonb_array_elements_text(c.related_topics) AS value
      )
  )
ORDER BY p.updated_at DESC
LIMIT @Take;";

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var rows = await connection.QueryAsync<WikiPageLinkRow>(
                sql,
                new { PageId = pageId, Take = Math.Clamp(take, 1, 20) }).ConfigureAwait(false);

            return Result<IReadOnlyList<WikiPageLink>>.Success(rows
                .Select(row => new WikiPageLink(row.Id, row.Topic, row.Title, row.Status, row.Version, row.UpdatedAt))
                .ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<WikiPageLink>>.Failure($"WRAGS related pages lookup failed. {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<WikiPageHistoryEntry>>> GetHistoryAsync(
        Guid pageId,
        int take,
        CancellationToken cancellationToken = default)
    {
        await _schema.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
SELECT
    id AS Id,
    page_id AS PageId,
    version AS Version,
    title AS Title,
    summary AS Summary,
    status AS Status,
    related_topics::text AS RelatedTopics,
    change_type AS ChangeType,
    changed_by AS ChangedBy,
    change_note AS ChangeNote,
    created_at AS CreatedAt
FROM wiki_page_history
WHERE page_id = @PageId
ORDER BY version DESC, created_at DESC
LIMIT @Take;";

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var rows = await connection.QueryAsync<WikiPageHistoryRow>(
                sql,
                new { PageId = pageId, Take = Math.Clamp(take, 1, 50) }).ConfigureAwait(false);

            return Result<IReadOnlyList<WikiPageHistoryEntry>>.Success(rows
                .Select(row => new WikiPageHistoryEntry(
                    row.Id,
                    row.PageId,
                    row.Version,
                    row.Title,
                    row.Summary,
                    row.Status,
                    Deserialize<string>(row.RelatedTopics),
                    row.ChangeType,
                    row.ChangedBy,
                    row.ChangeNote,
                    row.CreatedAt))
                .ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<WikiPageHistoryEntry>>.Failure($"WRAGS page history lookup failed. {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<WikiPage>>> UpsertAsync(
        IReadOnlyList<WikiPage> pages,
        CancellationToken cancellationToken = default)
    {
        await _schema.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        if (pages.Count == 0)
        {
            return Result<IReadOnlyList<WikiPage>>.Success(Array.Empty<WikiPage>());
        }

        const string sql = @"
INSERT INTO wiki_pages (
    id,
    topic,
    topic_normalized,
    title,
    title_normalized,
    summary,
    source_ids,
    citations,
    generated_from,
    version,
    status,
    score,
    rank,
    retrieval_strategy,
    primary_source_id,
    chunk_index,
    reviewed_by,
    reviewed_at,
    related_topics,
    created_at,
    updated_at)
VALUES (
    @Id,
    @Topic,
    @TopicNormalized,
    @Title,
    @TitleNormalized,
    @Summary,
    CAST(@SourceIds AS jsonb),
    CAST(@Citations AS jsonb),
    @GeneratedFrom,
    @Version,
    @Status,
    @Score,
    @Rank,
    @RetrievalStrategy,
    @PrimarySourceId,
    @ChunkIndex,
    @ReviewedBy,
    @ReviewedAt,
    CAST(@RelatedTopics AS jsonb),
    @CreatedAt,
    @UpdatedAt)
ON CONFLICT (topic_normalized, title_normalized, generated_from)
DO UPDATE SET
    summary = EXCLUDED.summary,
    source_ids = EXCLUDED.source_ids,
    citations = EXCLUDED.citations,
    version = wiki_pages.version + 1,
    status = EXCLUDED.status,
    score = EXCLUDED.score,
    rank = EXCLUDED.rank,
    retrieval_strategy = EXCLUDED.retrieval_strategy,
    primary_source_id = EXCLUDED.primary_source_id,
    chunk_index = EXCLUDED.chunk_index,
    related_topics = EXCLUDED.related_topics,
    reviewed_by = NULL,
    reviewed_at = NULL,
    updated_at = EXCLUDED.updated_at
RETURNING
    id AS Id,
    topic AS Topic,
    title AS Title,
    summary AS Summary,
    source_ids::text AS SourceIds,
    citations::text AS Citations,
    generated_from AS GeneratedFrom,
    version AS Version,
    status AS Status,
    score AS Score,
    rank AS Rank,
    retrieval_strategy AS RetrievalStrategy,
    primary_source_id AS PrimarySourceId,
    chunk_index AS ChunkIndex,
    related_topics::text AS RelatedTopics,
    reviewed_by AS ReviewedBy,
    reviewed_at AS ReviewedAt,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt;";

        const string existingSql = @"
SELECT
    id AS Id,
    topic AS Topic,
    title AS Title,
    summary AS Summary,
    source_ids::text AS SourceIds,
    citations::text AS Citations,
    generated_from AS GeneratedFrom,
    version AS Version,
    status AS Status,
    score AS Score,
    rank AS Rank,
    retrieval_strategy AS RetrievalStrategy,
    primary_source_id AS PrimarySourceId,
    chunk_index AS ChunkIndex,
    related_topics::text AS RelatedTopics,
    reviewed_by AS ReviewedBy,
    reviewed_at AS ReviewedAt,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt
FROM wiki_pages
WHERE topic_normalized = @TopicNormalized
  AND title_normalized = @TitleNormalized
  AND generated_from = @GeneratedFrom
LIMIT 1;";

        var saved = new List<WikiPage>();

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            foreach (var page in pages)
            {
                var topicNormalized = Normalize(page.Topic);
                var titleNormalized = Normalize(page.Title);
                var existing = await connection.QuerySingleOrDefaultAsync<WikiPageRow>(
                    existingSql,
                    new
                    {
                        TopicNormalized = topicNormalized,
                        TitleNormalized = titleNormalized,
                        page.GeneratedFrom
                    },
                    transaction).ConfigureAwait(false);
                if (existing is not null)
                {
                    await InsertHistoryAsync(
                        connection,
                        transaction,
                        existing,
                        "Regenerated",
                        null,
                        "Regenerated from current retrieval knowledge.").ConfigureAwait(false);
                }

                var row = await connection.QuerySingleAsync<WikiPageRow>(
                    sql,
                    new
                    {
                        page.Id,
                        page.Topic,
                        TopicNormalized = topicNormalized,
                        page.Title,
                        TitleNormalized = titleNormalized,
                        page.Summary,
                        SourceIds = JsonSerializer.Serialize(page.SourceIds, JsonOptions),
                        Citations = JsonSerializer.Serialize(page.Citations, JsonOptions),
                        page.GeneratedFrom,
                        page.Version,
                        page.Status,
                        page.Score,
                        page.Rank,
                        page.RetrievalStrategy,
                        page.PrimarySourceId,
                        page.ChunkIndex,
                        page.ReviewedBy,
                        page.ReviewedAt,
                        RelatedTopics = JsonSerializer.Serialize(page.RelatedTopics, JsonOptions),
                        page.CreatedAt,
                        UpdatedAt = DateTimeOffset.UtcNow
                    },
                    transaction).ConfigureAwait(false);

                saved.Add(Map(row));
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Result<IReadOnlyList<WikiPage>>.Success(saved);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<WikiPage>>.Failure($"WRAGS wiki persistence failed. {ex.Message}");
        }
    }

    public async Task<Result<WikiPage?>> UpdateStatusAsync(
        Guid pageId,
        string status,
        string? reviewedBy,
        CancellationToken cancellationToken = default)
    {
        await _schema.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
UPDATE wiki_pages
SET
    status = @Status,
    reviewed_by = CASE WHEN @Status IN ('Reviewed', 'Approved') THEN @ReviewedBy ELSE NULL END,
    reviewed_at = CASE WHEN @Status IN ('Reviewed', 'Approved') THEN @ReviewedAt ELSE NULL END,
    updated_at = @UpdatedAt
WHERE id = @PageId
RETURNING
    id AS Id,
    topic AS Topic,
    title AS Title,
    summary AS Summary,
    source_ids::text AS SourceIds,
    citations::text AS Citations,
    generated_from AS GeneratedFrom,
    version AS Version,
    status AS Status,
    score AS Score,
    rank AS Rank,
    retrieval_strategy AS RetrievalStrategy,
    primary_source_id AS PrimarySourceId,
    chunk_index AS ChunkIndex,
    related_topics::text AS RelatedTopics,
    reviewed_by AS ReviewedBy,
    reviewed_at AS ReviewedAt,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt;";

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var existing = await GetRowAsync(connection, transaction, pageId).ConfigureAwait(false);
            if (existing is null)
            {
                return Result<WikiPage?>.Success(null);
            }

            await InsertHistoryAsync(
                connection,
                transaction,
                existing,
                "Status",
                reviewedBy,
                $"Status changed to {status}.").ConfigureAwait(false);

            var row = await connection.QuerySingleOrDefaultAsync<WikiPageRow>(
                sql,
                new
                {
                    PageId = pageId,
                    Status = status,
                    ReviewedBy = string.IsNullOrWhiteSpace(reviewedBy) ? "system" : reviewedBy,
                    ReviewedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                transaction).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result<WikiPage?>.Success(row is null ? null : Map(row));
        }
        catch (Exception ex)
        {
            return Result<WikiPage?>.Failure($"WRAGS status update failed. {ex.Message}");
        }
    }

    public async Task<Result<WikiPage?>> UpdatePageAsync(
        Guid pageId,
        WikiPageEditRequest request,
        CancellationToken cancellationToken = default)
    {
        await _schema.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        const string sql = @"
UPDATE wiki_pages
SET
    title = @Title,
    title_normalized = @TitleNormalized,
    summary = @Summary,
    status = @Status,
    related_topics = CAST(@RelatedTopics AS jsonb),
    reviewed_by = CASE WHEN @Status IN ('Reviewed', 'Approved') THEN @EditedBy ELSE reviewed_by END,
    reviewed_at = CASE WHEN @Status IN ('Reviewed', 'Approved') THEN @UpdatedAt ELSE reviewed_at END,
    version = version + 1,
    updated_at = @UpdatedAt
WHERE id = @PageId
RETURNING
    id AS Id,
    topic AS Topic,
    title AS Title,
    summary AS Summary,
    source_ids::text AS SourceIds,
    citations::text AS Citations,
    generated_from AS GeneratedFrom,
    version AS Version,
    status AS Status,
    score AS Score,
    rank AS Rank,
    retrieval_strategy AS RetrievalStrategy,
    primary_source_id AS PrimarySourceId,
    chunk_index AS ChunkIndex,
    related_topics::text AS RelatedTopics,
    reviewed_by AS ReviewedBy,
    reviewed_at AS ReviewedAt,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt;";

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var existing = await GetRowAsync(connection, transaction, pageId).ConfigureAwait(false);
            if (existing is null)
            {
                return Result<WikiPage?>.Success(null);
            }

            await InsertHistoryAsync(
                connection,
                transaction,
                existing,
                "Edit",
                request.EditedBy,
                request.ChangeNote).ConfigureAwait(false);

            var title = string.IsNullOrWhiteSpace(request.Title) ? existing.Title : request.Title.Trim();
            var summary = request.Summary?.Trim() ?? existing.Summary;
            var status = string.IsNullOrWhiteSpace(request.Status) ? existing.Status : request.Status.Trim();
            var relatedTopics = request.RelatedTopics is { Count: > 0 }
                ? request.RelatedTopics
                    .Where(topic => !string.IsNullOrWhiteSpace(topic))
                    .Select(topic => topic.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(16)
                    .ToList()
                : Deserialize<string>(existing.RelatedTopics);
            var now = DateTimeOffset.UtcNow;

            var row = await connection.QuerySingleOrDefaultAsync<WikiPageRow>(
                sql,
                new
                {
                    PageId = pageId,
                    Title = title,
                    TitleNormalized = Normalize(title),
                    Summary = summary,
                    Status = status,
                    RelatedTopics = JsonSerializer.Serialize(relatedTopics, JsonOptions),
                    EditedBy = string.IsNullOrWhiteSpace(request.EditedBy) ? "system" : request.EditedBy,
                    UpdatedAt = now
                },
                transaction).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Result<WikiPage?>.Success(row is null ? null : Map(row));
        }
        catch (Exception ex)
        {
            return Result<WikiPage?>.Failure($"WRAGS page edit failed. {ex.Message}");
        }
    }

    private static async Task<WikiPageRow?> GetRowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid pageId)
    {
        const string sql = @"
SELECT
    id AS Id,
    topic AS Topic,
    title AS Title,
    summary AS Summary,
    source_ids::text AS SourceIds,
    citations::text AS Citations,
    generated_from AS GeneratedFrom,
    version AS Version,
    status AS Status,
    score AS Score,
    rank AS Rank,
    retrieval_strategy AS RetrievalStrategy,
    primary_source_id AS PrimarySourceId,
    chunk_index AS ChunkIndex,
    related_topics::text AS RelatedTopics,
    reviewed_by AS ReviewedBy,
    reviewed_at AS ReviewedAt,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt
FROM wiki_pages
WHERE id = @PageId
LIMIT 1;";

        return await connection.QuerySingleOrDefaultAsync<WikiPageRow>(
            sql,
            new { PageId = pageId },
            transaction).ConfigureAwait(false);
    }

    private static async Task InsertHistoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        WikiPageRow row,
        string changeType,
        string? changedBy,
        string? changeNote)
    {
        const string sql = @"
INSERT INTO wiki_page_history (
    id,
    page_id,
    version,
    title,
    summary,
    status,
    related_topics,
    change_type,
    changed_by,
    change_note,
    created_at)
VALUES (
    @Id,
    @PageId,
    @Version,
    @Title,
    @Summary,
    @Status,
    CAST(@RelatedTopics AS jsonb),
    @ChangeType,
    @ChangedBy,
    @ChangeNote,
    @CreatedAt);";

        await connection.ExecuteAsync(
            sql,
            new
            {
                Id = Guid.NewGuid(),
                PageId = row.Id,
                row.Version,
                row.Title,
                row.Summary,
                row.Status,
                row.RelatedTopics,
                ChangeType = string.IsNullOrWhiteSpace(changeType) ? "Change" : changeType,
                ChangedBy = string.IsNullOrWhiteSpace(changedBy) ? null : changedBy,
                ChangeNote = string.IsNullOrWhiteSpace(changeNote) ? null : changeNote,
                CreatedAt = DateTimeOffset.UtcNow
            },
            transaction).ConfigureAwait(false);
    }

    private static WikiPage Map(WikiPageRow row)
    {
        return new WikiPage(
            row.Id,
            row.Topic,
            row.Title,
            row.Summary,
            Deserialize<Guid>(row.SourceIds),
            Deserialize<string>(row.Citations),
            row.GeneratedFrom,
            row.Version,
            row.Status,
            row.Score,
            row.Rank,
            row.RetrievalStrategy,
            row.PrimarySourceId,
            row.ChunkIndex,
            Deserialize<string>(row.RelatedTopics),
            row.ReviewedBy,
            row.ReviewedAt,
            IsStale(row),
            GetStaleReason(row),
            row.CreatedAt,
            row.UpdatedAt);
    }

    private static bool IsStale(WikiPageRow row)
    {
        return row.Status.Equals("Stale", StringComparison.OrdinalIgnoreCase)
            || row.Status.Equals("NeedsReview", StringComparison.OrdinalIgnoreCase)
            || row.UpdatedAt < DateTimeOffset.UtcNow.AddDays(-14)
            || (row.SourceChangedAt.HasValue && row.SourceChangedAt.Value > row.UpdatedAt);
    }

    private static string? GetStaleReason(WikiPageRow row)
    {
        if (row.Status.Equals("Stale", StringComparison.OrdinalIgnoreCase))
        {
            return "Marked stale.";
        }

        if (row.Status.Equals("NeedsReview", StringComparison.OrdinalIgnoreCase))
        {
            return "Needs review.";
        }

        if (row.SourceChangedAt.HasValue && row.SourceChangedAt.Value > row.UpdatedAt)
        {
            return "Linked source changed after this page was last updated.";
        }

        return row.UpdatedAt < DateTimeOffset.UtcNow.AddDays(-14)
            ? "Older than 14 days."
            : null;
    }

    private static IReadOnlyList<T> Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<T>();
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<T>>(json, JsonOptions) ?? Array.Empty<T>();
        }
        catch (JsonException)
        {
            return Array.Empty<T>();
        }
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static string EscapeLike(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    private sealed class WikiPageRow
    {
        public Guid Id { get; init; }
        public string Topic { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string SourceIds { get; init; } = "[]";
        public string Citations { get; init; } = "[]";
        public string GeneratedFrom { get; init; } = "wrags";
        public int Version { get; init; }
        public string Status { get; init; } = "Generated";
        public float Score { get; init; }
        public int Rank { get; init; }
        public string RetrievalStrategy { get; init; } = "wrags";
        public Guid? PrimarySourceId { get; init; }
        public int? ChunkIndex { get; init; }
        public string RelatedTopics { get; init; } = "[]";
        public string? ReviewedBy { get; init; }
        public DateTimeOffset? ReviewedAt { get; init; }
        public DateTimeOffset? SourceChangedAt { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed class WikiPageLinkRow
    {
        public Guid Id { get; init; }
        public string Topic { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public int Version { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed class WikiPageHistoryRow
    {
        public Guid Id { get; init; }
        public Guid PageId { get; init; }
        public int Version { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string RelatedTopics { get; init; } = "[]";
        public string ChangeType { get; init; } = string.Empty;
        public string? ChangedBy { get; init; }
        public string? ChangeNote { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }
}
