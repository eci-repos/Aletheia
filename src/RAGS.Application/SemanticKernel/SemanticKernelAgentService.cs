using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Aletheia.RAGS.Application.SemanticKernel;

public sealed class SemanticKernelAgentService : IAgentService
{
    private readonly Kernel _kernel;
    private readonly IRagsService _ragsService;
    private readonly ITaxonomyProvider _taxonomyProvider;

    public SemanticKernelAgentService(
        Kernel kernel,
        IRagsService ragsService,
        ITaxonomyProvider taxonomyProvider)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _ragsService = ragsService ?? throw new ArgumentNullException(nameof(ragsService));
        _taxonomyProvider = taxonomyProvider ?? throw new ArgumentNullException(nameof(taxonomyProvider));
    }

    public async Task<Result<SummaryResponse>> SummarizeAsync(SummaryRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            var retrieval = await _ragsService.RetrieveAsync(
                new RetrievalRequest(request.Query, request.TopK),
                cancellationToken).ConfigureAwait(false);

            if (retrieval.IsFailure || retrieval.Value is null || !retrieval.Value.Any())
            {
                return Result<SummaryResponse>.Success(new SummaryResponse
                {
                    Summary = "No relevant content found to summarize.",
                    Sources = Array.Empty<SearchResult>()
                });
            }

            var content = string.Join("\n\n", retrieval.Value.Select(r => r.Chunk.Content));
            var summary = await GenerateSummaryAsync(content, cancellationToken).ConfigureAwait(false);

            return Result<SummaryResponse>.Success(new SummaryResponse
            {
                Summary = summary,
                Sources = retrieval.Value.ToList()
            });
        }
        catch (Exception ex)
        {
            return Result<SummaryResponse>.Failure($"Summarization failed: {ex.Message}");
        }
    }

    public async Task<Result<ExplanationResponse>> ExplainAsync(ExplanationRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            var retrieval = await _ragsService.RetrieveAsync(
                new RetrievalRequest(request.Query, request.TopK),
                cancellationToken).ConfigureAwait(false);

            if (retrieval.IsFailure || retrieval.Value is null || !retrieval.Value.Any())
            {
                return Result<ExplanationResponse>.Success(new ExplanationResponse
                {
                    Explanation = "No relevant content found to explain.",
                    Sources = Array.Empty<SearchResult>()
                });
            }

            var content = string.Join("\n\n", retrieval.Value.Select(r => r.Chunk.Content));
            var explanation = await GenerateExplanationAsync(request.Query, content, cancellationToken).ConfigureAwait(false);

            return Result<ExplanationResponse>.Success(new ExplanationResponse
            {
                Explanation = explanation,
                Sources = retrieval.Value.ToList()
            });
        }
        catch (Exception ex)
        {
            return Result<ExplanationResponse>.Failure($"Explanation failed: {ex.Message}");
        }
    }

    public async Task<Result<DiscoveryResponse>> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            var categoriesResult = await _taxonomyProvider.GetCategoriesAsync(cancellationToken).ConfigureAwait(false);
            var categories = categoriesResult.IsSuccess && categoriesResult.Value is not null
                ? categoriesResult.Value
                : new List<string>();

            var topics = new List<DiscoveryTopic>();
            var baseSearch = await _ragsService.RetrieveAsync(
                new RetrievalRequest(request.Topic, request.TopK),
                cancellationToken).ConfigureAwait(false);

            if (baseSearch.IsSuccess && baseSearch.Value is not null && baseSearch.Value.Any())
            {
                topics.Add(new DiscoveryTopic
                {
                    Title = request.Topic,
                    Description = await GenerateDiscoveryDescriptionAsync(request.Topic, baseSearch.Value, cancellationToken).ConfigureAwait(false),
                    Sources = baseSearch.Value.ToList()
                });
            }

            if (categories is not null)
            {
                foreach (var category in categories.Take(3))
                {
                    var tagQuery = $"{request.Topic} {category}";
                    var catResult = await _ragsService.RetrieveAsync(
                        new RetrievalRequest(tagQuery, Math.Min(3, request.TopK)),
                        cancellationToken).ConfigureAwait(false);

                    if (catResult.IsSuccess && catResult.Value is not null && catResult.Value.Any())
                    {
                        topics.Add(new DiscoveryTopic
                        {
                            Title = $"{request.Topic} in {category}",
                            Description = await GenerateDiscoveryDescriptionAsync(tagQuery, catResult.Value, cancellationToken).ConfigureAwait(false),
                            Sources = catResult.Value.ToList()
                        });
                    }
                }
            }

            if (!topics.Any())
            {
                topics.Add(new DiscoveryTopic
                {
                    Title = request.Topic,
                    Description = "No discoveries found for the given topic.",
                    Sources = Array.Empty<SearchResult>()
                });
            }

            return Result<DiscoveryResponse>.Success(new DiscoveryResponse { Topics = topics });
        }
        catch (Exception ex)
        {
            return Result<DiscoveryResponse>.Failure($"Discovery failed: {ex.Message}");
        }
    }

    private async Task<string> GenerateSummaryAsync(string content, CancellationToken cancellationToken)
    {
        var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddSystemMessage("You are a helpful summarization assistant. Summarize the following content concisely.");
        history.AddUserMessage(content);

        var response = await chatCompletion.GetChatMessageContentAsync(history, cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Content ?? "No summary could be generated.";
    }

    private async Task<string> GenerateExplanationAsync(string query, string content, CancellationToken cancellationToken)
    {
        var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddSystemMessage("You are a helpful explainer. Explain the topic based on the provided context.");
        history.AddUserMessage($"Explain \"{query}\" based on this context:\n\n{content}");

        var response = await chatCompletion.GetChatMessageContentAsync(history, cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Content ?? "No explanation could be generated.";
    }

    private async Task<string> GenerateDiscoveryDescriptionAsync(string topic, IReadOnlyList<SearchResult> results, CancellationToken cancellationToken)
    {
        var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
        var context = string.Join("\n\n", results.Select(r => r.Chunk.Content));
        var history = new ChatHistory();
        history.AddSystemMessage("You are a knowledge discovery assistant. Write a brief description of the topic based on the available sources.");
        history.AddUserMessage($"Topic: {topic}\n\nSources:\n{context}");

        var response = await chatCompletion.GetChatMessageContentAsync(history, cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Content ?? $"Primary results for '{topic}'.";
    }
}
