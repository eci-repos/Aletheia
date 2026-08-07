using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Application.GraphIntelligence;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;
using System.Text.Json;

namespace Aletheia.RAGS.Application.LazyGraphRAG;

/// <summary>
/// Discovers relationships between entities at query time using Semantic Kernel.
/// Defers all relationship intelligence work to retrieval time.
/// </summary>
public sealed class LazyRelationshipDiscoveryService : ILazyRelationshipDiscoveryService
{
    private readonly Kernel? _kernel;
    private readonly IGraphProvider _provider;

    public LazyRelationshipDiscoveryService(IGraphProvider provider, Kernel? kernel = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _kernel = kernel;
    }

    public async Task<Result<IReadOnlyList<ExtractedRelationship>>> DiscoverAtQueryTimeAsync(
        string query,
        IReadOnlyList<ExtractedEntity> entities,
        IGraphTraversalBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        if (!entities.Any())
            return Result<IReadOnlyList<ExtractedRelationship>>.Success(Array.Empty<ExtractedRelationship>());

        if (_kernel is null)
        {
            // Heuristic fallback: co-occurrence based on query terms
            return Result<IReadOnlyList<ExtractedRelationship>>.Success(
                BuildHeuristicRelationships(query, entities));
        }

        try
        {
            var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            
            var entityDescriptions = new StringBuilder();
            foreach (var e in entities)
            {
                entityDescriptions.AppendLine($"- {e.Name} ({e.Type}): {e.Description ?? "N/A"}");
            }

            history.AddSystemMessage(
                "You are a relationship extraction assistant. Given a query and a list of entities, " +
                "identify semantic relationships between pairs of entities that are relevant to the query. " +
                "Return a JSON array of objects with 'sourceName', 'targetName', 'type' (e.g., works_with, located_in, part_of, founded_by), " +
                "and 'description' fields. Only include relationships strongly supported by the query context.");
            history.AddUserMessage($"Query: {query}\n\nEntities:\n{entityDescriptions}");

            var response = await chatCompletion.GetChatMessageContentAsync(history, cancellationToken: cancellationToken).ConfigureAwait(false);
            budget?.RecordTokens(TokenUsageHelper.GetTotalTokens(response));
            var parsed = TryParseJsonRelationships(response.Content ?? string.Empty, entities);
            
            if (parsed.Any())
                return Result<IReadOnlyList<ExtractedRelationship>>.Success(parsed);

            return Result<IReadOnlyList<ExtractedRelationship>>.Success(
                BuildHeuristicRelationships(query, entities));
        }
        catch
        {
            return Result<IReadOnlyList<ExtractedRelationship>>.Success(
                BuildHeuristicRelationships(query, entities));
        }
    }

    public async Task<Result> PersistAsync(
        IReadOnlyList<ExtractedRelationship> relationships,
        CancellationToken cancellationToken = default)
    {
        foreach (var rel in relationships)
        {
            var edge = new GraphEdge(
                rel.Id,
                rel.SourceId,
                rel.TargetId,
                rel.Type,
                new Dictionary<string, object>
                {
                    ["confidence"] = rel.Confidence,
                    ["description"] = rel.Description ?? string.Empty,
                });

            await _provider.CreateRelationshipAsync(edge, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }

    private static IReadOnlyList<ExtractedRelationship> TryParseJsonRelationships(string json, IReadOnlyList<ExtractedEntity> entities)
    {
        var relationships = new List<ExtractedRelationship>();
        var entityMap = entities.ToDictionary(e => e.Name.ToLowerInvariant().Trim(), e => e.Id);

        try
        {
            var startIndex = json.IndexOf('[');
            var endIndex = json.LastIndexOf(']');
            if (startIndex < 0 || endIndex < 0 || endIndex <= startIndex)
                return relationships;

            var arrayJson = json.Substring(startIndex, endIndex - startIndex + 1);
            var items = JsonSerializer.Deserialize<List<JsonRelationship>>(arrayJson);

            if (items is not null)
            {
                foreach (var item in items)
                {
                    var sourceName = item.sourceName?.ToLowerInvariant().Trim();
                    var targetName = item.targetName?.ToLowerInvariant().Trim();

                    if (string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(targetName))
                        continue;

                    if (entityMap.TryGetValue(sourceName, out var sourceId) &&
                        entityMap.TryGetValue(targetName, out var targetId))
                    {
                        relationships.Add(new ExtractedRelationship
                        {
                            SourceId = sourceId,
                            TargetId = targetId,
                            Type = item.type ?? "related_to",
                            Description = item.description,
                            Confidence = 0.8
                        });
                    }
                }
            }
        }
        catch
        {
            // JSON parse failed
        }

        return relationships;
    }

    private static IReadOnlyList<ExtractedRelationship> BuildHeuristicRelationships(string query, IReadOnlyList<ExtractedEntity> entities)
    {
        var relationships = new List<ExtractedRelationship>();
        var lowerQuery = query.ToLowerInvariant();
        var queryEntities = entities.Where(e => lowerQuery.Contains(e.Name.ToLowerInvariant())).ToList();

        for (var i = 0; i < queryEntities.Count - 1; i++)
        {
            for (var j = i + 1; j < queryEntities.Count; j++)
            {
                relationships.Add(new ExtractedRelationship
                {
                    SourceId = queryEntities[i].Id,
                    TargetId = queryEntities[j].Id,
                    Type = "co-mentioned",
                    Confidence = 0.5
                });
            }
        }

        return relationships;
    }

    private class JsonRelationship
    {
        public string? sourceName { get; set; }
        public string? targetName { get; set; }
        public string? type { get; set; }
        public string? description { get; set; }
    }
}
