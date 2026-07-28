using System.Text;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Application.SemanticKernel;

public static class RetrievalAugmentedPromptBuilder
{
    private const int DefaultMaxResults = 5;
    private const int DefaultMaxChunkCharacters = 1600;

    public static string Build(
        string userMessage,
        IReadOnlyList<SearchResult> results,
        int maxResults = DefaultMaxResults,
        int maxChunkCharacters = DefaultMaxChunkCharacters,
        KnowledgeSource? source = null,
        CopilotAnswerProfileOptions? answerProfile = null,
        IReadOnlyList<string>? defaultAreas = null)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            throw new ArgumentException("User message is required.", nameof(userMessage));
        }

        if (results is null)
        {
            throw new ArgumentNullException(nameof(results));
        }

        if (maxResults <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), "Max results must be greater than zero.");
        }

        if (maxChunkCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChunkCharacters), "Max chunk characters must be greater than zero.");
        }

        var ranked = results
            .Where(r => r is not null)
            .OrderBy(r => r.Rank <= 0 ? int.MaxValue : r.Rank)
            .ThenByDescending(r => r.Score)
            .Take(maxResults)
            .ToList();

        if (ranked.Count == 0)
        {
            return userMessage;
        }

        var prompt = new StringBuilder();
        prompt.AppendLine("You are an agent of the Aletheia platform.");
        prompt.AppendLine("Your knowledge is limited to the provided WRAGS and RAGS context below, which comes from the Aletheia Knowledge Estate (registered Repository artifacts and WRAGS Wiki pages).");
        prompt.AppendLine("When the user's question concerns repository content such as RFPs, contracts, requirements, or wiki pages, you must ground your answer exclusively in the retrieved context.");
        prompt.AppendLine("Do not use general internet knowledge, LLM training data, or external facts to answer repository-specific questions.");
        prompt.AppendLine("If the requested information is not present in the retrieved context, state clearly that it was not found in the Aletheia repository rather than inventing an answer.");
        if (answerProfile?.RequireCitations != false)
        {
            prompt.AppendLine("Cite supporting evidence with bracketed citation numbers such as [1], and reference the source artifact or wiki page when possible.");
        }
        else
        {
            prompt.AppendLine("If context is present, do not claim you lack access to those artifacts.");
        }

        if (source is not null)
        {
            prompt.AppendLine(FormattableString.Invariant($"Resolved KB artifact: {source.SourceName} ({source.SourceId})"));
        }

        var areas = GetAreas(answerProfile, defaultAreas);
        if (areas.Count > 0)
        {
            prompt.AppendLine($"Cover these configured focus areas when relevant: {string.Join(", ", areas)}.");
        }

        if (!string.IsNullOrWhiteSpace(answerProfile?.OutputFormat))
        {
            prompt.AppendLine($"Format the answer as {answerProfile.OutputFormat.Trim()}.");
        }

        foreach (var instruction in answerProfile?.Instructions ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(instruction))
            {
                prompt.AppendLine(instruction.Trim());
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("Retrieved context:");

        for (var i = 0; i < ranked.Count; i++)
        {
            var result = ranked[i];
            var citationNumber = i + 1;
            var rank = result.Rank > 0 ? result.Rank : citationNumber;
            var citations = result.Citations.Count > 0
                ? string.Join(", ", result.Citations)
                : result.Chunk.SourceId.ToString();

            prompt.AppendLine(FormattableString.Invariant($"[{citationNumber}] Rank: {rank}; Score: {result.Score:0.###}; Strategy: {result.RetrievalStrategy}"));
            prompt.AppendLine(FormattableString.Invariant($"SourceId: {result.Chunk.SourceId}; ChunkId: {result.Chunk.Id}; ChunkIndex: {result.Chunk.Index}"));
            prompt.AppendLine(FormattableString.Invariant($"Citations: {citations}"));
            prompt.AppendLine("Content:");
            prompt.AppendLine(Trim(result.Chunk.Content, maxChunkCharacters));
            prompt.AppendLine();
        }

        prompt.AppendLine("User question:");
        prompt.AppendLine(userMessage);

        return prompt.ToString();
    }

    private static IReadOnlyList<string> GetAreas(CopilotAnswerProfileOptions? answerProfile, IReadOnlyList<string>? defaultAreas)
    {
        var areas = answerProfile?.Areas is { Count: > 0 }
            ? answerProfile.Areas
            : defaultAreas;

        return areas?
            .Where(area => !string.IsNullOrWhiteSpace(area))
            .Select(area => area.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<string>();
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
