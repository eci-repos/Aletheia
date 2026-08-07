using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Aletheia.RAGS.Application.GraphIntelligence;

public sealed class EntityExtractionService : IEntityExtractionService
{
    private readonly Kernel _kernel;

    public EntityExtractionService(Kernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    }

    public async Task<Result<IReadOnlyList<ExtractedEntity>>> DiscoverAsync(string text, IGraphTraversalBudget? budget = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Result<IReadOnlyList<ExtractedEntity>>.Success(Array.Empty<ExtractedEntity>());

        try
        {
            var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddSystemMessage(
                "You are an entity extraction assistant. Extract named entities from the provided text. " +
                "Return a JSON array of objects with 'name' (string), 'type' (string, e.g., Person, Organization, Location, Technology, Concept), " +
                "and 'description' (string) fields. Example: [{\"name\":\"Microsoft\",\"type\":\"Organization\",\"description\":\"A multinational technology corporation\"}]");
            history.AddUserMessage(text);

            var response = await chatCompletion.GetChatMessageContentAsync(history, cancellationToken: cancellationToken).ConfigureAwait(false);
            budget?.RecordTokens(TokenUsageHelper.GetTotalTokens(response));
            var content = response.Content ?? string.Empty;

            var parsed = TryParseJsonEntities(content);
            if (parsed.Any())
            {
                return Result<IReadOnlyList<ExtractedEntity>>.Success(parsed);
            }

            var entities = SimpleExtractEntities(text);
            return Result<IReadOnlyList<ExtractedEntity>>.Success(entities);
        }
        catch
        {
            var entities = SimpleExtractEntities(text);
            return Result<IReadOnlyList<ExtractedEntity>>.Success(entities);
        }
    }

    private static IReadOnlyList<ExtractedEntity> TryParseJsonEntities(string json)
    {
        var entities = new List<ExtractedEntity>();

        try
        {
            // Extract JSON array from response (handle markdown code blocks)
            var startIndex = json.IndexOf('[');
            var endIndex = json.LastIndexOf(']');
            if (startIndex < 0 || endIndex < 0 || endIndex <= startIndex)
                return entities;

            var arrayJson = json.Substring(startIndex, endIndex - startIndex + 1);
            var items = System.Text.Json.JsonSerializer.Deserialize<List<JsonEntity>>(arrayJson);

            if (items is not null)
            {
                foreach (var item in items)
                {
                    if (!string.IsNullOrWhiteSpace(item.name))
                    {
                        entities.Add(new ExtractedEntity
                        {
                            Name = item.name.Trim(),
                            Type = item.type ?? "entity",
                            Description = item.description,
                            Confidence = 0.85
                        });
                    }
                }
            }
        }
        catch
        {
            // JSON parse failed
        }

        return entities;
    }

    private class JsonEntity
    {
        public string name { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public string? description { get; set; }
    }

    public Task<Result<IReadOnlyList<ExtractedEntity>>> ClassifyAsync(IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default)
    {
        // TODO: Use Semantic Kernel for entity type classification
        return Task.FromResult(Result<IReadOnlyList<ExtractedEntity>>.Success(entities));
    }

    public Task<Result<IReadOnlyList<ExtractedEntity>>> ScoreConfidenceAsync(IReadOnlyList<ExtractedEntity> entities, CancellationToken cancellationToken = default)
    {
        // TODO: Implement confidence scoring based on context richness
        return Task.FromResult(Result<IReadOnlyList<ExtractedEntity>>.Success(entities));
    }

    private static IReadOnlyList<ExtractedEntity> SimpleExtractEntities(string text)
    {
        var words = text.Split(new[] { ' ', '.', ',', ';', ':', '!', '?', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
            "and", "or", "but", "of", "in", "on", "at", "to", "for", "with", "by",
            "this", "that", "these", "those", "it", "its", "from", "as", "has", "have"
        };

        return words
            .Select(w => w.Trim().ToLowerInvariant())
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Distinct()
            .Select(w => new ExtractedEntity { Name = w, Type = "keyword", Confidence = 0.5 })
            .ToList();
    }
}
