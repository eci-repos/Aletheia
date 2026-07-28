using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;
using Npgsql;

namespace Aletheia.Repository.Infrastructure.PostgreSQL.Versioning;

public sealed class PostgreSqlVersioningService : IVersioningService
{
    private const string CreateVersionFailedMessage = "Version creation failed.";
    private const string ListVersionsFailedMessage = "Version listing failed.";

    private readonly PostgreSqlConnectionFactory _connectionFactory;

    public PostgreSqlVersioningService(PostgreSqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<Result<FileDescriptor>> CreateVersionAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        var newVersion = Guid.NewGuid().ToString("N")[..8];
        var versionedDescriptor = new FileDescriptor(descriptor.FileId, descriptor.FileName, newVersion);

        const string sql = @"
            INSERT INTO file_metadata (file_id, file_name, version, content_type, size_bytes, uploaded_at, tags)
            SELECT file_id, file_name, @NewVersion, content_type, size_bytes, uploaded_at, tags
            FROM file_metadata
            WHERE file_id = @FileId AND (version = @Version OR (@Version IS NULL AND version IS NULL))
            LIMIT 1
            RETURNING file_id AS ""FileId"", file_name AS ""FileName"", version AS ""Version""";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var row = await connection.QueryFirstOrDefaultAsync<VersionRow>(sql, new
            {
                descriptor.FileId,
                Version = descriptor.Version,
                NewVersion = newVersion
            }).ConfigureAwait(false);

            if (row is null)
            {
                return Result<FileDescriptor>.Failure(CreateVersionFailedMessage);
            }

            return Result<FileDescriptor>.Success(new FileDescriptor(row.FileId, row.FileName, row.Version));
        }
        catch (PostgresException ex)
        {
            return Result<FileDescriptor>.Failure($"{CreateVersionFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyCollection<FileDescriptor>>> ListVersionsAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        const string sql = @"
            SELECT file_id AS ""FileId"", file_name AS ""FileName"", version AS ""Version""
            FROM file_metadata
            WHERE file_id = @FileId
            ORDER BY uploaded_at DESC";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<VersionRow>(sql, new { descriptor.FileId }).ConfigureAwait(false);
        var descriptors = rows.Select(r => new FileDescriptor(r.FileId, r.FileName, r.Version)).ToList();

        return Result<IReadOnlyCollection<FileDescriptor>>.Success(descriptors);
    }

    private record VersionRow
    {
        public Guid FileId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string? Version { get; set; }
    }
}
