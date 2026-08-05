using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;
using Npgsql;

namespace Aletheia.RAGS.Infrastructure.PostgreSQL.Taxonomy;

public sealed class TaxonomyService : ITaxonomyProvider
{
    private const string GetCategoriesFailedMessage = "Failed to retrieve categories.";
    private const string GetTagsFailedMessage = "Failed to retrieve tags.";

    private readonly PostgreSqlConnectionFactory _connectionFactory;

    public TaxonomyService(PostgreSqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<Result<IReadOnlyCollection<string>>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT name FROM categories ORDER BY name";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var names = await connection.QueryAsync<string>(sql).ConfigureAwait(false);
            return Result<IReadOnlyCollection<string>>.Success(names.ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyCollection<string>>.Failure($"{GetCategoriesFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyCollection<string>>> GetTagsAsync(string category, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Category is required.", nameof(category));
        }

        const string sql = @"
            SELECT t.name
            FROM taxonomy_tags t
            JOIN categories c ON c.id = t.category_id
            WHERE c.name = @CategoryName
            ORDER BY t.name";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var names = await connection.QueryAsync<string>(sql, new { CategoryName = category }).ConfigureAwait(false);
            var normalized = names
                .Select(KnowledgeTermNormalizer.NormalizeLabel)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Result<IReadOnlyCollection<string>>.Success(normalized);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyCollection<string>>.Failure($"{GetTagsFailedMessage} {ex.Message}");
        }
    }
}
