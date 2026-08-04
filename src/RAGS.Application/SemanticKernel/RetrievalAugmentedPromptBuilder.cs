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
        IReadOnlyList<string>? defaultAreas = null,
        string? scopeInstruction = null,
        ChatAgentOptions? chatAgentOptions = null,
        string? orchestrationInstructions = null,
        IReadOnlyList<DocumentTemplateSection>? sectionOutline = null)
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

        var options = chatAgentOptions ?? new ChatAgentOptions();
        var prompt = new StringBuilder();
        prompt.AppendLine(options.Role);
        prompt.AppendLine(options.RepositoryDescription);
        prompt.AppendLine(options.Mandate);
        prompt.AppendLine("Your knowledge is strictly limited to the provided WRAGS and RAGS context below, which comes from the Aletheia Knowledge Estate (registered Repository artifacts and WRAGS Wiki pages).");
        prompt.AppendLine("You have no access to general internet knowledge, LLM training data, market data, or external facts.");
        prompt.AppendLine("When the user's question concerns repository content such as RFPs, contracts, requirements, or wiki pages, you must ground your answer exclusively in the retrieved context.");
        prompt.AppendLine("If the requested information is not present in the retrieved context, state clearly that it was not found in the Aletheia repository rather than inventing an answer.");
        prompt.AppendLine("Do not provide statistics, dimensions, counts, status breakdowns, or summaries unless they are explicitly present in the retrieved context.");
        prompt.AppendLine("Do not expose internal retrieval implementation details such as graph communities, community IDs, chunk counts, retrieval strategies, or index structure as the answer. Use them only as hidden provenance. The user-facing answer must be phrased in terms of documents, requirements, features, evidence, and citations.");
        prompt.AppendLine("When the user asks for a summary of one or more documents, each document summary must state that document’s stated purpose, theme, or main topic as presented in its opening or summary sections (for example its Project Summary), and must not omit it when it is present in the retrieved context.");
        var sourceGroups = ranked
            .GroupBy(result => result.Chunk.SourceId)
            .Select(group => new SourceContextGroup(
                group.Key,
                ResolveSourceName(group),
                group.OrderBy(result => result.Rank <= 0 ? int.MaxValue : result.Rank)
                    .ThenByDescending(result => result.Score)
                    .ToList()))
            .ToList();
        prompt.AppendLine(FormattableString.Invariant($"You are provided with context for {sourceGroups.Count} distinct document source(s)."));
        prompt.AppendLine("Preserve source identity. Facts from one source must never be used to describe another source.");
        prompt.AppendLine("For multi-source questions, provide a separate section for every source below, using the Source Name as the section header. Do not merge, blend, or omit source sections.");
        prompt.AppendLine("If a question names one source, answer only from that source's block and ignore facts in other source blocks.");
        prompt.AppendLine("If a retrieved summary says that details are located elsewhere but does not include the requested details, do not present that summary as a substitute for the facts.");
        if (!string.IsNullOrWhiteSpace(orchestrationInstructions))
        {
            prompt.AppendLine("Repository orchestration playbook:");
            prompt.AppendLine(orchestrationInstructions.Trim());
        }

        if ((answerProfile?.RequireCitations ?? false) || options.BehaviorFlags.CiteSources)
        {
            prompt.AppendLine("Cite supporting evidence with bracketed citation numbers such as [1], and reference the source artifact or wiki page when possible.");
            prompt.AppendLine("Every substantive claim must be backed by a citation from the retrieved context.");
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

        if (!string.IsNullOrWhiteSpace(scopeInstruction))
        {
            prompt.AppendLine(scopeInstruction.Trim());
        }

        if (sectionOutline is { Count: > 0 })
        {
            prompt.AppendLine("Template-guided summary structure: the documents follow a template with these ordered sections.");
            prompt.AppendLine("Produce each document summary in the following order, opening with the project's stated nature/purpose from the document's opening or Project Summary section:");
            for (var i = 0; i < sectionOutline.Count; i++)
            {
                var section = sectionOutline[i];
                prompt.AppendLine(FormattableString.Invariant(
                    $"  {i + 1}. {section.Title}{(string.IsNullOrWhiteSpace(section.Description) ? string.Empty : $" - {section.Description}")}"));
            }

            prompt.AppendLine("For any section not covered by the retrieved evidence, state explicitly that the document does not cover it; never invent content for a section.");
        }

        foreach (var instruction in answerProfile?.Instructions ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(instruction))
            {
                prompt.AppendLine(instruction.Trim());
            }
        }

        prompt.AppendLine();
        AppendRetrievedContext(prompt, ranked, sourceGroups, maxChunkCharacters);

        prompt.AppendLine("If the retrieved context does not directly address the explicit technical intent for a named document, say the retrieved source block did not contain that detail. Do not borrow details from another source.");
        prompt.AppendLine(FormattableString.Invariant($"Your response must contain {sourceGroups.Count} separate source section(s) when the question asks across multiple documents. Failure to list a separate section for every source ID provided is a grounding violation."));

        prompt.AppendLine("User question:");
        prompt.AppendLine(userMessage);

        return prompt.ToString();
    }

    /// <summary>
    /// Builds a focused, end-user document brief prompt: the document's nature/purpose comes
    /// first (opening/Project Summary evidence), then the canonical template's sections in
    /// order, each grounded in per-section retrieved evidence, cited, in plain language with
    /// no chunk/community/graph jargon.
    /// </summary>
    public static string BuildDocumentBrief(
        string sourceName,
        IReadOnlyList<SearchResult> results,
        IReadOnlyList<DocumentTemplateSection>? sectionOutline = null)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("Source name is required.", nameof(sourceName));
        }

        if (results is null)
        {
            throw new ArgumentNullException(nameof(results));
        }

        var ranked = results
            .Where(r => r is not null)
            .OrderBy(r => r.Rank <= 0 ? int.MaxValue : r.Rank)
            .ThenByDescending(r => r.Score)
            .ToList();

        var prompt = new StringBuilder();
        prompt.AppendLine("You are writing a document brief for an end user of the Aletheia document repository.");
        prompt.AppendLine("The document follows a canonical template. Produce a readable brief in plain language that:");
        prompt.AppendLine("  1. Opens with the document's stated nature and purpose as presented in its opening or Project Summary evidence (do not omit it when present).");
        prompt.AppendLine("  2. Covers the template's sections in the exact order listed below, each grounded in the retrieved evidence for that section.");
        prompt.AppendLine("  3. Cites supporting evidence with bracketed numbers such as [1] referencing the document.");
        prompt.AppendLine("  4. States explicitly when the retrieved evidence does not cover a section; never invent content.");
        prompt.AppendLine("You must never mention chunks, communities, graph internals, retrieval strategies, index structure, or internal provenance in the brief itself.");
        prompt.AppendLine("Do not expose internal retrieval implementation details as the answer; use them only as hidden provenance.");

        if (sectionOutline is { Count: > 0 })
        {
            prompt.AppendLine();
            prompt.AppendLine("Canonical template sections (in order):");
            for (var i = 0; i < sectionOutline.Count; i++)
            {
                var section = sectionOutline[i];
                prompt.AppendLine(FormattableString.Invariant(
                    $"  {i + 1}. {section.Title}{(string.IsNullOrWhiteSpace(section.Description) ? string.Empty : $" - {section.Description}")}"));
            }
        }

        if (ranked.Count == 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("Retrieved context: no evidence was available for this document.");
            prompt.AppendLine("Document:");
            prompt.AppendLine(sourceName);
            return prompt.ToString();
        }

        var sourceGroups = ranked
            .GroupBy(result => result.Chunk.SourceId)
            .Select(group => new SourceContextGroup(
                group.Key,
                ResolveSourceName(group),
                group.OrderBy(result => result.Rank <= 0 ? int.MaxValue : result.Rank)
                    .ThenByDescending(result => result.Score)
                    .ToList()))
            .ToList();

        prompt.AppendLine(FormattableString.Invariant($"The evidence below comes from document: {sourceName}."));
        prompt.AppendLine();
        AppendRetrievedContext(prompt, ranked, sourceGroups, DefaultMaxChunkCharacters);

        prompt.AppendLine("Write the document brief now. The opening paragraph must state the document's nature and purpose.");
        return prompt.ToString();
    }

    private static void AppendRetrievedContext(
        StringBuilder prompt,
        IReadOnlyList<SearchResult> ranked,
        IReadOnlyList<SourceContextGroup> sourceGroups,
        int maxChunkCharacters)
    {
        prompt.AppendLine("Retrieved context:");

        var citationNumber = 1;
        foreach (var group in sourceGroups)
        {
            prompt.AppendLine(FormattableString.Invariant($"--- START SOURCE: {group.SourceName} (ID: {group.SourceId}) ---"));
            prompt.AppendLine("Boundary rule: use only the evidence inside this source block for this source's section.");

            foreach (var result in group.Results)
            {
                var rank = result.Rank > 0 ? result.Rank : citationNumber;
                var citations = result.Citations.Count > 0
                    ? string.Join(", ", result.Citations)
                    : result.Chunk.SourceId.ToString();

                prompt.AppendLine(FormattableString.Invariant($"[{citationNumber}] Rank: {rank}; Score: {result.Score:0.###}; Strategy: {result.RetrievalStrategy}"));
                prompt.AppendLine(FormattableString.Invariant($"SourceName: {group.SourceName}; SourceId: {result.Chunk.SourceId}; ChunkId: {result.Chunk.Id}; ChunkIndex: {result.Chunk.Index}"));
                prompt.AppendLine(FormattableString.Invariant($"Citations: {citations}"));
                prompt.AppendLine("Content:");
                prompt.AppendLine(Trim(result.Chunk.Content, maxChunkCharacters));
                prompt.AppendLine();
                citationNumber++;
            }

            prompt.AppendLine(FormattableString.Invariant($"--- END SOURCE: {group.SourceName} (ID: {group.SourceId}) ---"));
            prompt.AppendLine();
        }
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

    private static string ResolveSourceName(IGrouping<Guid, SearchResult> group)
    {
        foreach (var result in group)
        {
            var citation = result.Citations
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)
                    && !Guid.TryParse(value.Trim(), out _));
            if (!string.IsNullOrWhiteSpace(citation))
            {
                return citation.Trim();
            }
        }

        return group.Key.ToString();
    }

    private sealed record SourceContextGroup(
        Guid SourceId,
        string SourceName,
        IReadOnlyList<SearchResult> Results);
}
