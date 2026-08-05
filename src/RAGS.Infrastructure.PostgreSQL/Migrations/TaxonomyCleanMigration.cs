using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Aletheia.RAGS.Application;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;

namespace Aletheia.RAGS.Infrastructure.PostgreSQL.Migrations
{
    public sealed class TaxonomyCleanMigration
    {
        private readonly PostgreSqlConnectionFactory _connectionFactory;
        private readonly ConfigurableTermNormalizer _normalizer;
        private readonly ILogger<TaxonomyCleanMigration> _logger;

        public TaxonomyCleanMigration(
            PostgreSqlConnectionFactory connectionFactory,
            IOptions<TaxonomyOptions> options,
            ILogger<TaxonomyCleanMigration> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            var opts = options?.Value ?? new TaxonomyOptions();
            // Reuse the ConfigurableTermNormalizer implementation.
            _normalizer = new ConfigurableTermNormalizer(Options.Create(opts), NullLogger<ConfigurableTermNormalizer>.Instance);
            _logger = logger;
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            await using var conn = _connectionFactory.CreateConnection();
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            const string selectSql = @"SELECT id, name FROM taxonomy_tags;";
            var tags = await conn.QueryAsync<TagRecord>(selectSql, transaction: tx).ConfigureAwait(false);

            foreach (var tag in tags)
            {
                var normalized = _normalizer.Normalize(tag.Name);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    // Delete tag sources first to satisfy FK.
                    await conn.ExecuteAsync(
                        @"DELETE FROM taxonomy_tag_sources WHERE tag_id = @TagId;",
                        new { TagId = tag.Id }, tx).ConfigureAwait(false);
                    await conn.ExecuteAsync(
                        @"DELETE FROM taxonomy_tags WHERE id = @TagId;",
                        new { TagId = tag.Id }, tx).ConfigureAwait(false);
                    _logger?.LogInformation("Removed stop‑word tag {TagId}/{Name}", tag.Id, tag.Name);
                }
                else if (!string.Equals(normalized, tag.Name, StringComparison.OrdinalIgnoreCase))
                {
                    await conn.ExecuteAsync(
                        @"UPDATE taxonomy_tags SET name = @NewName WHERE id = @TagId;",
                        new { TagId = tag.Id, NewName = normalized }, tx).ConfigureAwait(false);
                    _logger?.LogInformation("Renamed tag {TagId} from '{Old}' to '{New}'", tag.Id, tag.Name, normalized);
                }
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        private sealed record TagRecord(Guid Id, string Name);
    }
}

