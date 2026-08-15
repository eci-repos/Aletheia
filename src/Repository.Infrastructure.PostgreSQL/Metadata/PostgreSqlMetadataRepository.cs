using System.Data;
using System.Text.Json;
using Aletheia.Foundation.Audit;
using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;
using Npgsql;

namespace Aletheia.Repository.Infrastructure.PostgreSQL.Metadata;

public sealed class PostgreSqlMetadataRepository : IMetadataRepository
{
    private const string GetFailedMessage = "Metadata retrieval failed.";
    private const string SaveFailedMessage = "Metadata save failed.";
    private const string SearchFailedMessage = "Metadata search failed.";
    private const string DeleteFailedMessage = "Metadata delete failed.";
    private const string MetadataSelectColumns = @"
                file_id AS ""FileId"",
                file_name AS ""FileName"",
                version AS ""Version"",
                content_type AS ""ContentType"",
                size_bytes AS ""SizeBytes"",
                uploaded_at AS ""UploadedAt"",
                tags AS ""Tags"",
                content_hash AS ""ContentHash"",
                template_name AS ""TemplateName"",
                theme AS ""Theme"",
                template_status AS ""TemplateStatus"",
                created_at AS ""CreatedAt"",
                created_by_id AS ""CreatedById"",
                created_by_type AS ""CreatedByType"",
                created_by_name AS ""CreatedByName"",
                last_modified_at AS ""LastModifiedAt"",
                last_modified_by_id AS ""LastModifiedById"",
                last_modified_by_type AS ""LastModifiedByType"",
                last_modified_by_name AS ""LastModifiedByName""";

    private readonly PostgreSqlConnectionFactory _connectionFactory;

    public PostgreSqlMetadataRepository(PostgreSqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<Result<FileMetadata>> GetAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        var sql = $@"
            SELECT {MetadataSelectColumns}
            FROM file_metadata
            WHERE file_id = @FileId AND (version = @Version OR (@Version IS NULL AND version IS NULL))
            LIMIT 1";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var row = await connection.QueryFirstOrDefaultAsync<MetadataRow>(sql, new
        {
            descriptor.FileId,
            Version = descriptor.Version
        }).ConfigureAwait(false);

        if (row is null)
        {
            return Result<FileMetadata>.Failure(GetFailedMessage);
        }

        return Result<FileMetadata>.Success(MapToMetadata(row));
    }

    public async Task<Result<FileMetadata?>> GetByFileIdAsync(Guid fileId, string? version = null, CancellationToken cancellationToken = default)
    {
        var sql = $@"
            SELECT {MetadataSelectColumns}
            FROM file_metadata
            WHERE file_id = @FileId AND (version = @Version OR (@Version IS NULL AND version IS NULL))
            LIMIT 1";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var row = await connection.QueryFirstOrDefaultAsync<MetadataRow>(sql, new
        {
            FileId = fileId,
            Version = version
        }).ConfigureAwait(false);

        return row is null
            ? Result<FileMetadata?>.Success(null)
            : Result<FileMetadata?>.Success(MapToMetadata(row));
    }

    public async Task<Result<FileMetadata>> SaveAsync(FileMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        var sql = $@"
            INSERT INTO file_metadata (file_id, file_name, version, content_type, size_bytes, uploaded_at, tags, content_hash, template_name, theme, template_status,
                                       created_at, created_by_id, created_by_type, created_by_name,
                                       last_modified_at, last_modified_by_id, last_modified_by_type, last_modified_by_name)
            VALUES (@FileId, @FileName, @Version, @ContentType, @SizeBytes, @UploadedAt, @Tags::jsonb, @ContentHash, @TemplateName, @Theme, @TemplateStatus,
                    @CreatedAt, @CreatedById, @CreatedByType, @CreatedByName,
                    @LastModifiedAt, @LastModifiedById, @LastModifiedByType, @LastModifiedByName)
            ON CONFLICT (file_id, COALESCE(version, ''))
            DO UPDATE SET
                file_name = EXCLUDED.file_name,
                content_type = EXCLUDED.content_type,
                size_bytes = EXCLUDED.size_bytes,
                uploaded_at = EXCLUDED.uploaded_at,
                tags = EXCLUDED.tags,
                content_hash = EXCLUDED.content_hash,
                template_name = EXCLUDED.template_name,
                theme = EXCLUDED.theme,
                template_status = EXCLUDED.template_status,
                last_modified_at = EXCLUDED.last_modified_at,
                last_modified_by_id = EXCLUDED.last_modified_by_id,
                last_modified_by_type = EXCLUDED.last_modified_by_type,
                last_modified_by_name = EXCLUDED.last_modified_by_name
            RETURNING {MetadataSelectColumns}";

        var descriptor = metadata.Descriptor;
        var tags = JsonSerializer.Serialize(metadata.Tags);
        var row = MapToRow(metadata);

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await connection.QueryFirstAsync<MetadataRow>(sql, row with { Tags = tags }).ConfigureAwait(false);
            return Result<FileMetadata>.Success(MapToMetadata(result));
        }
        catch (PostgresException ex)
        {
            return Result<FileMetadata>.Failure($"{SaveFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<PagedResult<FileMetadata>>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        const string countSql = @"
            SELECT COUNT(*) FROM file_metadata
            WHERE (@Query IS NULL OR file_name ILIKE '%' || @Query || '%')
              AND (@Filters IS NULL OR tags @> @Filters::jsonb)";

        var dataSql = $@"
            SELECT {MetadataSelectColumns}
            FROM file_metadata
            WHERE (@Query IS NULL OR file_name ILIKE '%' || @Query || '%')
              AND (@Filters IS NULL OR tags @> @Filters::jsonb)
            ORDER BY uploaded_at DESC
            LIMIT @PageSize OFFSET @Offset";

        var filters = request.Filters.Count > 0 ? JsonSerializer.Serialize(request.Filters) : null;
        var offset = (request.PageNumber - 1) * request.PageSize;

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var totalCount = await connection.ExecuteScalarAsync<int>(countSql, new
        {
            Query = request.Query,
            Filters = filters
        }).ConfigureAwait(false);

        var rows = await connection.QueryAsync<MetadataRow>(dataSql, new
        {
            Query = request.Query,
            Filters = filters,
            request.PageSize,
            Offset = offset
        }).ConfigureAwait(false);

        var items = rows.Select(MapToMetadata).ToList();
        var pagedResult = new PagedResult<FileMetadata>(items, request.PageNumber, request.PageSize, totalCount);
        return Result<PagedResult<FileMetadata>>.Success(pagedResult);
    }

    public async Task<Result> DeleteAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        const string sql = @"
            DELETE FROM file_metadata
            WHERE file_id = @FileId AND (version = @Version OR (@Version IS NULL AND version IS NULL))";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var deleted = await connection.ExecuteAsync(sql, new
            {
                descriptor.FileId,
                Version = descriptor.Version
            }).ConfigureAwait(false);

            return deleted > 0
                ? Result.Success()
                : Result.Failure("Metadata not found.");
        }
        catch (PostgresException ex)
        {
            return Result.Failure($"{DeleteFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<FileMetadata?>> FindByContentHashAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return Result<FileMetadata?>.Success(null);
        }

        var sql = $@"
            SELECT {MetadataSelectColumns}
            FROM file_metadata
            WHERE content_hash = @ContentHash
            ORDER BY uploaded_at DESC
            LIMIT 1";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var row = await connection.QueryFirstOrDefaultAsync<MetadataRow>(sql, new { ContentHash = contentHash }).ConfigureAwait(false);
            return Result<FileMetadata?>.Success(row is null ? null : MapToMetadata(row));
        }
        catch (PostgresException ex)
        {
            return Result<FileMetadata?>.Failure($"{SearchFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<FileMetadata>>> ListContentHashDuplicatesAsync(CancellationToken cancellationToken = default)
    {
        var sql = $@"
            SELECT {MetadataSelectColumns}
            FROM file_metadata
            WHERE content_hash IS NOT NULL
              AND content_hash IN (
                  SELECT content_hash
                  FROM file_metadata
                  WHERE content_hash IS NOT NULL
                  GROUP BY content_hash
                  HAVING COUNT(*) > 1
              )
            ORDER BY content_hash, uploaded_at DESC";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await connection.QueryAsync<MetadataRow>(sql).ConfigureAwait(false);
            var items = rows.Select(MapToMetadata).ToList();
            return Result<IReadOnlyList<FileMetadata>>.Success(items);
        }
        catch (PostgresException ex)
        {
            return Result<IReadOnlyList<FileMetadata>>.Failure($"{SearchFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result> SetTemplateAsync(
        Guid fileId,
        string? templateName,
        IReadOnlyList<string>? themes,
        string? templateStatus = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE file_metadata
            SET template_name = @TemplateName,
                theme = @Theme,
                template_status = @TemplateStatus
            WHERE file_id = @FileId";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await connection.ExecuteAsync(sql, new
            {
                FileId = fileId,
                TemplateName = templateName,
                Theme = themes is null ? null : themes.ToArray(),
                TemplateStatus = templateStatus
            }).ConfigureAwait(false);
            return Result.Success();
        }
        catch (PostgresException ex)
        {
            return Result.Failure($"{SaveFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result> SetLastIngestedAtAsync(Guid fileId, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE file_metadata
            SET last_ingested_at = @Timestamp
            WHERE file_id = @FileId";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await connection.ExecuteAsync(sql, new { FileId = fileId, Timestamp = timestamp.UtcDateTime }).ConfigureAwait(false);
            return Result.Success();
        }
        catch (PostgresException ex)
        {
            return Result.Failure($"{SaveFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<Guid>>> GetSourcesMissingIngestionAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT fm.file_id AS ""FileId""
            FROM file_metadata fm
            WHERE fm.last_ingested_at IS NULL
              AND NOT EXISTS (
                  SELECT 1 FROM embeddings e
                  WHERE e.source_id = fm.file_id
              )
            GROUP BY fm.file_id";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await connection.QueryAsync<Guid>(sql).ConfigureAwait(false);
            return Result<IReadOnlyList<Guid>>.Success(rows.ToList());
        }
        catch (PostgresException ex)
        {
            return Result<IReadOnlyList<Guid>>.Failure($"{SearchFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<FileThemeRow>>> ListThemeRowsAsync(CancellationToken cancellationToken = default)
    {
        var sql = $@"
            SELECT file_id AS ""FileId"",
                   file_name AS ""FileName"",
                   template_name AS ""TemplateName"",
                   theme AS ""Theme"",
                   template_status AS ""TemplateStatus""
            FROM file_metadata
            ORDER BY uploaded_at DESC";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await connection.QueryAsync<ThemeRow>(sql).ConfigureAwait(false);
            var items = rows.Select(row => new FileThemeRow(row.FileId, row.FileName, row.TemplateName, row.Theme, row.TemplateStatus)).ToList();
            return Result<IReadOnlyList<FileThemeRow>>.Success(items);
        }
        catch (PostgresException ex)
        {
            return Result<IReadOnlyList<FileThemeRow>>.Failure($"{SearchFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<FileThemeRow>>> ListUncategorizedAsync(CancellationToken cancellationToken = default)
    {
        var sql = $@"
            SELECT file_id AS ""FileId"",
                   file_name AS ""FileName"",
                   template_name AS ""TemplateName"",
                   theme AS ""Theme"",
                   template_status AS ""TemplateStatus""
            FROM file_metadata
            WHERE template_status IS NULL OR template_status <> 'Canonical'
            ORDER BY uploaded_at DESC";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await connection.QueryAsync<ThemeRow>(sql).ConfigureAwait(false);
            var items = rows.Select(row => new FileThemeRow(row.FileId, row.FileName, row.TemplateName, row.Theme, row.TemplateStatus)).ToList();
            return Result<IReadOnlyList<FileThemeRow>>.Success(items);
        }
        catch (PostgresException ex)
        {
            return Result<IReadOnlyList<FileThemeRow>>.Failure($"{SearchFailedMessage} {ex.Message}");
        }
    }
    private static FileMetadata MapToMetadata(MetadataRow row)
    {
        var descriptor = new FileDescriptor(row.FileId, row.FileName, row.Version);

        var tags = string.IsNullOrWhiteSpace(row.Tags)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(row.Tags)!;

        AuditInfo? auditInfo = null;
        if (!string.IsNullOrWhiteSpace(row.CreatedById))
        {
            var createdBy = new AuditActor(row.CreatedById!, row.CreatedByType!, row.CreatedByName);
            AuditActor? lastModifiedBy = null;
            if (!string.IsNullOrWhiteSpace(row.LastModifiedById))
            {
                lastModifiedBy = new AuditActor(row.LastModifiedById!, row.LastModifiedByType!, row.LastModifiedByName);
            }
            auditInfo = new AuditInfo(row.CreatedAt!.Value, createdBy, row.LastModifiedAt, lastModifiedBy);
        }

        return new FileMetadata(descriptor, row.ContentType, row.SizeBytes, row.UploadedAt, tags, auditInfo, row.ContentHash, row.TemplateName, row.Theme, row.TemplateStatus);
    }

    private static MetadataRow MapToRow(FileMetadata metadata)
    {
        var d = metadata.Descriptor;
        var audit = metadata.AuditInfo;
        return new MetadataRow
        {
            FileId = d.FileId,
            FileName = d.FileName,
            Version = d.Version,
            ContentType = metadata.ContentType,
            SizeBytes = metadata.SizeBytes,
            UploadedAt = metadata.UploadedAt,
            Tags = JsonSerializer.Serialize(metadata.Tags),
            ContentHash = metadata.ContentHash,
            TemplateName = metadata.TemplateName,
            Theme = metadata.Theme?.ToArray(),
            TemplateStatus = metadata.TemplateStatus,
            CreatedAt = audit?.CreatedAt,
            CreatedById = audit?.CreatedBy.ActorId,
            CreatedByType = audit?.CreatedBy.ActorType,
            CreatedByName = audit?.CreatedBy.DisplayName,
            LastModifiedAt = audit?.LastModifiedAt,
            LastModifiedById = audit?.LastModifiedBy?.ActorId,
            LastModifiedByType = audit?.LastModifiedBy?.ActorType,
            LastModifiedByName = audit?.LastModifiedBy?.DisplayName
        };
    }

    private record MetadataRow
    {
        public Guid FileId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTimeOffset UploadedAt { get; set; }
        public string Tags { get; set; } = "{}";
        public string? ContentHash { get; set; }
        public string? TemplateName { get; set; }
        public string[]? Theme { get; set; }
        public string? TemplateStatus { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public string? CreatedById { get; set; }
        public string? CreatedByType { get; set; }
        public string? CreatedByName { get; set; }
        public DateTimeOffset? LastModifiedAt { get; set; }
        public string? LastModifiedById { get; set; }
        public string? LastModifiedByType { get; set; }
        public string? LastModifiedByName { get; set; }
    }

    private record ThemeRow
    {
        public Guid FileId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string? TemplateName { get; set; }
        public string[]? Theme { get; set; }
        public string? TemplateStatus { get; set; }
    }
}
