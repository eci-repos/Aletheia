using System.Text.Json;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Aletheia.RAGS.Application.Lexicon;

/// <summary>
/// The semantic (LLM) recognition layer of grounded fact extraction. Instructs the model to quote
/// the exact source span each fact was read from; the fidelity gate (<c>FactVerifier</c>) then
/// confirms the span exists and the value parses before anything is stored. On any failure the
/// proposer returns no proposals — it never fabricates facts into the pipeline.
/// </summary>
public sealed class SemanticKernelFactProposer : IFactProposer
{
    private readonly Kernel _kernel;

    public SemanticKernelFactProposer(Kernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    }

    public async Task<Result<IReadOnlyList<ProposedFact>>> ProposeAsync(
        string text,
        IReadOnlyList<LexiconConcept> concepts,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Result<IReadOnlyList<ProposedFact>>.Success(Array.Empty<ProposedFact>());
        }

        try
        {
            var knownConcepts = concepts is { Count: > 0 }
                ? string.Join(", ", concepts.Select(c => $"{c.Key} ({c.Label})"))
                : "(none)";
            var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddSystemMessage(
                "You are a fact extraction assistant. Extract key facts from the provided document text. " +
                $"Known concepts: {knownConcepts}. " +
                "For each fact, return a JSON array of objects with exactly three fields: " +
                "\"concept\" (the known concept key that best fits, or a short new key if none fits), " +
                "\"value\" (the fact's value exactly as written in the text), and " +
                "\"span\" (the EXACT contiguous text from the document that supports this fact, quoted verbatim). " +
                "Example: [{\"concept\":\"due_date\",\"value\":\"February 24, 2022\",\"span\":\"Proposal Due Date: February 24, 2022, at 2:00 p.m. EST\"}]. " +
                "Only include facts explicitly stated in the text. Do not infer or invent facts.");
            history.AddUserMessage(text);

            var response = await chatCompletion
                .GetChatMessageContentAsync(history, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return Result<IReadOnlyList<ProposedFact>>.Success(TryParseProposals(response.Content ?? string.Empty));
        }
        catch
        {
            return Result<IReadOnlyList<ProposedFact>>.Success(Array.Empty<ProposedFact>());
        }
    }

    private static IReadOnlyList<ProposedFact> TryParseProposals(string json)
    {
        var proposals = new List<ProposedFact>();
        try
        {
            // Extract the JSON array from the response (handle markdown code blocks).
            var startIndex = json.IndexOf('[');
            var endIndex = json.LastIndexOf(']');
            if (startIndex < 0 || endIndex < 0 || endIndex <= startIndex)
            {
                return proposals;
            }

            var arrayJson = json.Substring(startIndex, endIndex - startIndex + 1);
            var items = JsonSerializer.Deserialize<List<JsonProposal>>(arrayJson);
            if (items is null)
            {
                return proposals;
            }

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.span) || string.IsNullOrWhiteSpace(item.value))
                {
                    continue;
                }

                proposals.Add(new ProposedFact
                {
                    ConceptHint = item.concept ?? string.Empty,
                    Value = item.value.Trim(),
                    SourceSpan = item.span.Trim()
                });
            }
        }
        catch
        {
            // JSON parse failed — return nothing rather than storing unverified facts.
        }

        return proposals;
    }

    private sealed class JsonProposal
    {
        public string? concept { get; set; }
        public string? value { get; set; }
        public string? span { get; set; }
    }
}
