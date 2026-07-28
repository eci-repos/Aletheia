using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Interfaces;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;

namespace Aletheia.RAGS.Application.GraphIntelligence;

public sealed class HierarchicalSummaryService : IHierarchicalSummaryService
{
    private readonly Kernel _kernel;
    private readonly IGraphProvider _provider;
    private readonly IGraphSummaryService _graphSummary;

    public HierarchicalSummaryService(
        Kernel kernel,
        IGraphProvider provider,
        IGraphSummaryService graphSummary)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _graphSummary = graphSummary ?? throw new ArgumentNullException(nameof(graphSummary));
    }

    public async Task<Result<string>> SummarizeDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var nodeResult = await _provider.GetNodeAsync(documentId, cancellationToken).ConfigureAwait(false);
        if (nodeResult.IsFailure || nodeResult.Value is null)
        {
            return Result<string>.Failure($"Document '{documentId}' not found in the graph.");
        }

        var node = nodeResult.Value;
        var neighbors = await _provider.GetNeighborsAsync(documentId, cancellationToken).ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.AppendLine($"Document: {node.Label}");
        sb.AppendLine($"Type: {node.Type}");

        if (node.Properties.Any())
        {
            sb.AppendLine("Properties:");
            foreach (var prop in node.Properties.Where(p => p.Value is not null && p.Key != "content"))
            {
                sb.AppendLine($"  - {prop.Key}: {prop.Value}");
            }

            if (node.Properties.TryGetValue("content", out var content) && content is string text && text.Length > 0)
            {
                var preview = text.Length > 2000 ? text[..2000] + "..." : text;
                sb.AppendLine($"\nContent Preview:\n{preview}");
            }
        }

        if (neighbors.IsSuccess && neighbors.Value is not null && neighbors.Value.Any())
        {
            var entityNeighbors = neighbors.Value.Where(n => n.Type != "Source").Take(20).ToList();
            sb.AppendLine($"\nExtracted Entities ({entityNeighbors.Count}):");
            foreach (var entity in entityNeighbors)
            {
                sb.AppendLine($"  - {entity.Label} ({entity.Type})");
            }
        }

        sb.AppendLine("\nGenerate a concise summary of this document, highlighting its main topics and key entities.");

        var summary = await GenerateSummaryAsync(sb.ToString(), cancellationToken).ConfigureAwait(false);
        return Result<string>.Success(summary);
    }

    public Task<Result<string>> SummarizeEntityAsync(string entityId, CancellationToken cancellationToken = default)
    {
        return _graphSummary.SummarizeEntityAsync(entityId, cancellationToken);
    }

    public Task<Result<string>> SummarizeCommunityAsync(string communityId, CancellationToken cancellationToken = default)
    {
        return _graphSummary.SummarizeCommunityAsync(communityId, cancellationToken);
    }

    public async Task<Result<string>> SummarizeKnowledgeAreaAsync(string areaId, CancellationToken cancellationToken = default)
    {
        var nodeResult = await _provider.GetNodeAsync(areaId, cancellationToken).ConfigureAwait(false);
        if (nodeResult.IsFailure || nodeResult.Value is null)
        {
            return Result<string>.Failure($"Knowledge area '{areaId}' not found in the graph.");
        }

        var node = nodeResult.Value;
        var subgraph = await _provider.GetSubgraphAsync(areaId, 2, cancellationToken).ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.AppendLine($"Knowledge Area: {node.Label}");
        sb.AppendLine($"Type: {node.Type}");

        if (node.Properties.Any())
        {
            sb.AppendLine("Properties:");
            foreach (var prop in node.Properties.Where(p => p.Value is not null))
            {
                sb.AppendLine($"  - {prop.Key}: {prop.Value}");
            }
        }

        if (subgraph.IsSuccess && subgraph.Value is not null && subgraph.Value.Any())
        {
            var related = subgraph.Value.Where(n => n.Id != areaId).Take(20).ToList();
            sb.AppendLine($"\nRelated Entities ({related.Count}):");
            foreach (var entity in related)
            {
                sb.AppendLine($"  - {entity.Label} ({entity.Type})");
            }
        }

        sb.AppendLine("\nGenerate a concise summary of this knowledge area, describing its scope and related concepts.");

        var summary = await GenerateSummaryAsync(sb.ToString(), cancellationToken).ConfigureAwait(false);
        return Result<string>.Success(summary);
    }

    public Task<Result<string>> SummarizeGlobalAsync(CancellationToken cancellationToken = default)
    {
        return _graphSummary.SummarizeGlobalAsync(cancellationToken);
    }

    private async Task<string> GenerateSummaryAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddSystemMessage(
                "You are a precise knowledge graph summarization assistant. " +
                "Produce concise, factual summaries without speculation. Use only the provided data.");
            history.AddUserMessage(prompt);

            var response = await chatCompletion.GetChatMessageContentAsync(history, cancellationToken: cancellationToken).ConfigureAwait(false);
            return response.Content?.Trim() ?? "Summary generation produced no content.";
        }
        catch
        {
            return prompt.Replace("Generate a ", "Generated ").Replace("based on the information above", "from available data");
        }
    }
}
