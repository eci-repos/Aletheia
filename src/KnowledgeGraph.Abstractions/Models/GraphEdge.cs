namespace Aletheia.KnowledgeGraph.Abstractions.Models;

public sealed class GraphEdge
{
    public GraphEdge(string id, string sourceId, string targetId, string relationshipType, IReadOnlyDictionary<string, object>? properties = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Edge ID is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentException("Source ID is required.", nameof(sourceId));
        }

        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new ArgumentException("Target ID is required.", nameof(targetId));
        }

        if (string.IsNullOrWhiteSpace(relationshipType))
        {
            throw new ArgumentException("Relationship type is required.", nameof(relationshipType));
        }

        Id = id;
        SourceId = sourceId;
        TargetId = targetId;
        RelationshipType = relationshipType;
        Properties = properties ?? new Dictionary<string, object>();
    }

    public string Id { get; }

    public string SourceId { get; }

    public string TargetId { get; }

    public string RelationshipType { get; }

    public IReadOnlyDictionary<string, object> Properties { get; }
}
