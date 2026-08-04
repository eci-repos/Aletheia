using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.SemanticKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Aletheia.RAGS.Application.DocumentBriefs;

/// <summary>
/// Generates document brief text through the configured Semantic Kernel chat completion
/// service, using <see cref="RetrievalAugmentedPromptBuilder.BuildDocumentBrief"/> so the
/// brief opens with the document's nature/purpose and follows the canonical template's
/// ordered sections. Falls back to a deterministic structured brief when the LLM is
/// unavailable so a wiki page is still produced.
/// </summary>
public sealed class SemanticKernelDocumentBriefGenerator : IDocumentBriefGenerator
{
    private const int MaxFallbackChunkCharacters = 1_200;

    private readonly Kernel _kernel;
    private readonly ILogger<SemanticKernelDocumentBriefGenerator> _logger;

    public SemanticKernelDocumentBriefGenerator(
        Kernel kernel,
        ILogger<SemanticKernelDocumentBriefGenerator>? logger = null)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _logger = logger ?? NullLogger<SemanticKernelDocumentBriefGenerator>.Instance;
    }

    public async Task<Result<string>> GenerateAsync(
        DocumentBriefRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var prompt = RetrievalAugmentedPromptBuilder.BuildDocumentBrief(
            request.SourceName,
            request.Evidence,
            request.Sections);

        try
        {
            var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddSystemMessage(
                "You are Aletheia's document brief writer. You produce readable, plain-language " +
                "briefs about repository documents for end users. You must never mention chunks, " +
                "communities, graph internals, retrieval strategies, or index structure. " +
                "Every substantive claim must be grounded in the provided document evidence and cited.");
            history.AddUserMessage(prompt);

            var response = await chatCompletion
                .GetChatMessageContentAsync(history, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = response.Content?.Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                return Result<string>.Failure("Document brief generation produced no content.");
            }

            return Result<string>.Success(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM document brief generation failed for {SourceName}; using deterministic fallback.", request.SourceName);
            return Result<string>.Success(BuildFallbackBrief(request));
        }
    }

    private static string BuildFallbackBrief(DocumentBriefRequest request)
    {
        var brief = new System.Text.StringBuilder();
        brief.AppendLine($"# {request.SourceName}");
        brief.AppendLine();
        brief.AppendLine("Document brief (generated from retrieved document content):");
        brief.AppendLine();

        var seen = new HashSet<Guid>();
        var index = 0;
        foreach (var result in request.Evidence)
        {
            if (!seen.Add(result.Chunk.Id))
            {
                continue;
            }

            var content = Trim(result.Chunk.Content, MaxFallbackChunkCharacters);
            brief.AppendLine($"[{++index}] {content}");
            brief.AppendLine();
        }

        return brief.ToString().Trim();
    }

    private static string Trim(string content, int maxCharacters)
    {
        if (content.Length <= maxCharacters)
        {
            return content;
        }

        return content[..maxCharacters].TrimEnd() + "...";
    }
}
