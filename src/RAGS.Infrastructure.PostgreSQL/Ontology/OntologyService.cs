using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
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
            return Result<IReadOnlyCollection<string>>.Success(names.ToList());
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
            WHERE s.name = @EntityName
            ORDER BY r.relationship_type, t.name";

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await connection.QueryAsync<RelationshipRow>(sql, new { EntityName = entity }).ConfigureAwait(false);
            var dict = rows.ToDictionary(r => r.TargetName, r => r.RelationshipType);
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
