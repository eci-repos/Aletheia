using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Aletheia.RAGS.Application.GraphIntelligence;

public sealed class RelationshipExtractionService : IRelationshipExtractionService
{
    private readonly Kernel _kernel;

    public RelationshipExtractionService(Kernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    }

    public async Task<Result<IReadOnlyList<ExtractedRelationship>>> DiscoverAsync(string text, IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default)
    {
        if (!entities.Any())
            return Result<IReadOnlyList<ExtractedRelationship>>.Success(Array.Empty<ExtractedRelationship>());

        try
        {
            var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();

            var entityDescriptions = string.Join("\n", entities.Select(e => $"- {e.Name} ({e.Type}): {e.Description ?? "N/A"}"));
            var entityIds = string.Join("\n", entities.Select(e => $"- {e.Name} => ID: {e.Id}"));

            history.AddSystemMessage(
                "You are a relationship extraction assistant. Given a text and a list of entities, " +
                "identify semantic relationships between the entities. Return a JSON array of objects with " +
                "'sourceName', 'targetName', 'type' (e.g., works_with, located_in, part_of, founded_by), " +
                "and 'description' fields. Example: [{\"sourceName\":\"Bill Gates\",\"targetName\":\"Microsoft\",\"type\":\"founded_by\",\"description\":\"Bill Gates co-founded Microsoft\"}]");
            history.AddUserMessage($"Text: {text[..Math.Min(2000, text.Length)]}\n\nEntities:\n{entityDescriptions}\n\nEntity IDs:\n{entityIds}");

            var response = await chatCompletion.GetChatMessageContentAsync(history, cancellationToken: cancellationToken).ConfigureAwait(false);
            var content = response.Content ?? string.Empty;

            var parsed = TryParseJsonRelationships(content, entities);
            if (parsed.Any())
                return Result<IReadOnlyList<ExtractedRelationship>>.Success(parsed);

            // Fallback: co-occurrence
            return Result<IReadOnlyList<ExtractedRelationship>>.Success(
                BuildCooccurrenceRelationships(entities));
        }
        catch
        {
            return Result<IReadOnlyList<ExtractedRelationship>>.Success(
                BuildCooccurrenceRelationships(entities));
        }
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
            var items = System.Text.Json.JsonSerializer.Deserialize<List<JsonRelationship>>(arrayJson);

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
                        Type = NormalizeRelationshipType(item.type),
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

    private static IReadOnlyList<ExtractedRelationship> BuildCooccurrenceRelationships(IReadOnlyList<ExtractedEntity> entities)
    {
        var relationships = new List<ExtractedRelationship>();
        var entityList = entities.ToList();

        for (var i = 0; i < entityList.Count - 1; i++)
        {
            for (var j = i + 1; j < entityList.Count; j++)
            {
                relationships.Add(new ExtractedRelationship
                {
                    SourceId = entityList[i].Id,
                    TargetId = entityList[j].Id,
                    Type = "co_occurs_with",
                    Confidence = 0.5
                });
            }
        }

        return relationships;
    }

    private static string NormalizeRelationshipType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return "related_to";
        }

        var normalized = new string(type.Trim()
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_')
            .ToArray());

        while (normalized.Contains("__", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        }

        normalized = normalized.Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "related_to" : normalized;
    }

    private class JsonRelationship
    {
        public string? sourceName { get; set; }
        public string? targetName { get; set; }
        public string? type { get; set; }
        public string? description { get; set; }
    }

    public Task<Result<IReadOnlyList<ExtractedRelationship>>> ClassifyAsync(IReadOnlyList<ExtractedRelationship> relationships, CancellationToken cancellationToken = default)
    {
        // TODO: Use Semantic Kernel for relationship type classification
        return Task.FromResult(Result<IReadOnlyList<ExtractedRelationship>>.Success(relationships));
    }

    public Task<Result<IReadOnlyList<ExtractedRelationship>>> ScoreConfidenceAsync(IReadOnlyList<ExtractedRelationship> relationships, CancellationToken cancellationToken = default)
    {
        // TODO: Implement confidence scoring based on evidence strength
        return Task.FromResult(Result<IReadOnlyList<ExtractedRelationship>>.Success(relationships));
    }
}
