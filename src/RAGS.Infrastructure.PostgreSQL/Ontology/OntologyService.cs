using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Dapper;
using Npgsql;

namespace Aletheia.RAGS.Infrastructure.PostgreSQL.Ontology;

public sealed class OntologyService : IOntologyProvider
{
    private const string GetEntitiesFailedMessage = "Failed to retrieve entities.";
    private const string GetRelationshipsFailedMessage = "Failed to retrieve relationships.";

    private readonly PostgreSqlConnectionFactory _connectionFactory;

    public OntologyService(PostgreSqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<Result<IReadOnlyCollection<string>>> GetEntitiesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT name FROM ontology_entities ORDER BY name";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var names = await connection.QueryAsync<string>(sql).ConfigureAwait(false);
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
            return Result<IReadOnlyCollection<string>>.Failure($"{GetEntitiesFailedMessage} {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyDictionary<string, string>>> GetRelationshipsAsync(string entity, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entity))
        {
            throw new ArgumentException("Entity is required.", nameof(entity));
        }

        const string sql = @"
            SELECT r.relationship_type AS RelationshipType, t.name AS TargetName
            FROM ontology_relationships r
            JOIN ontology_entities s ON s.id = r.source_entity_id
            JOIN ontology_entities t ON t.id = r.target_entity_id
            WHERE s.name = ANY(@EntityNames)
            ORDER BY r.relationship_type, t.name";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var aliases = KnowledgeTermNormalizer.GetLookupAliases(entity).ToArray();
            var rows = await connection.QueryAsync<RelationshipRow>(sql, new { EntityNames = aliases }).ConfigureAwait(false);
            var dict = rows
                .GroupBy(r => r.TargetName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().RelationshipType,
                    StringComparer.OrdinalIgnoreCase);
            return Result<IReadOnlyDictionary<string, string>>.Success(dict);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure($"{GetRelationshipsFailedMessage} {ex.Message}");
        }
    }

    private record RelationshipRow
    {
        public string RelationshipType { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
    }
}
