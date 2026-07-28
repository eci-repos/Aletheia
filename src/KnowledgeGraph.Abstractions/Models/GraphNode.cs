namespace Aletheia.KnowledgeGraph.Abstractions.Models;

public sealed class GraphNode
{
    public GraphNode(string id, string label, string type, IReadOnlyDictionary<string, object>? properties = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Node ID is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Label is required.", nameof(label));
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("Type is required.", nameof(type));
        }

        Id = id;
        Label = label;
        Type = type;
        Properties = properties ?? new Dictionary<string, object>();
    }

    public string Id { get; }

    public string Label { get; }

    public string Type { get; }

    public IReadOnlyDictionary<string, object> Properties { get; }
}
