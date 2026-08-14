using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Application.Lexicon;

/// <summary>
/// The fidelity gate of grounded fact extraction. A proposal becomes a stored fact only when:
/// (1) the quoted source span actually exists in the extracted text (whitespace-tolerant match),
/// and (2) the value parses against the concept's value pattern. Anything else is dropped — nothing
/// enters the knowledge base that is not verifiable in the source. Verified facts are anchored to
/// their page/offset via the same page-boundary logic as <c>ChunkingPipeline</c>.
/// </summary>
public static class FactVerifier
{
    public static IReadOnlyList<DocumentFact> Verify(
        IReadOnlyList<ProposedFact> proposals,
        string text,
        IReadOnlyList<TextPage>? pages,
        IReadOnlyList<LexiconConcept> concepts,
        string? templateName = null)
    {
        var facts = new List<DocumentFact>();
        if (proposals is null || proposals.Count == 0 || string.IsNullOrWhiteSpace(text))
        {
            return facts;
        }

        // Sprint 71: template_scope enforcement — a scoped concept applies only to documents of that
        // template; unscoped concepts apply everywhere. A document with no template (Uncategorized)
        // sees only the unscoped concepts.
        var applicable = concepts.Where(c => IsApplicable(c, templateName)).ToList();
        var conceptsByKey = applicable
            .Where(c => !string.IsNullOrWhiteSpace(c.Key))
            .ToDictionary(c => c.Key, c => c, StringComparer.OrdinalIgnoreCase);
        var conceptsByAlias = applicable
            .SelectMany(c => (c.Aliases ?? Array.Empty<string>()).Select(a => (Alias: a, Concept: c)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Alias))
            .ToDictionary(x => x.Alias, x => x.Concept, StringComparer.OrdinalIgnoreCase);

        var (collapsedText, positionMap) = WhitespaceCollapser.Collapse(text);

        foreach (var proposal in proposals)
        {
            var concept = ResolveConcept(proposal.ConceptHint, conceptsByKey, conceptsByAlias);
            var span = WhitespaceCollapser.Collapse(proposal.SourceSpan ?? string.Empty).Text;
            if (string.IsNullOrWhiteSpace(span))
            {
                continue;
            }

            // Fidelity gate 1: the span must exist in the source text.
            var index = collapsedText.IndexOf(span, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            // Fidelity gate 2: the value must parse against the concept's value pattern.
            if (!FactValueParser.TryParse(concept?.ValuePattern, proposal.Value, out var normalizedValue))
            {
                continue;
            }

            var originalOffset = positionMap[index];
            var (pageNumber, offsetInPage) = ResolvePage(pages, originalOffset);
            facts.Add(new DocumentFact
            {
                ConceptKey = concept?.Key ?? proposal.ConceptHint?.Trim() ?? "unknown",
                Value = normalizedValue!,
                SourceSpan = proposal.SourceSpan!.Trim(),
                PageNumber = pageNumber,
                OffsetInPage = offsetInPage,
                Status = "verified"
            });
        }

        return facts;
    }

    /// <summary>
    /// Whether a concept applies to a document with the given canonical template name. Unscoped
    /// concepts (no <c>TemplateScope</c>) apply everywhere; a scoped concept applies only when the
    /// document's template matches its scope (case-insensitive). Shared by the verifier and the
    /// unmapped-term recorder so both see the same concept set.
    /// </summary>
    public static bool IsApplicable(LexiconConcept concept, string? templateName)
    {
        if (string.IsNullOrWhiteSpace(concept.TemplateScope))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(templateName)
            && string.Equals(concept.TemplateScope, templateName, StringComparison.OrdinalIgnoreCase);
    }

    private static LexiconConcept? ResolveConcept(
        string? hint,
        IReadOnlyDictionary<string, LexiconConcept> byKey,
        IReadOnlyDictionary<string, LexiconConcept> byAlias)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return null;
        }

        if (byKey.TryGetValue(hint, out var byKeyMatch))
        {
            return byKeyMatch;
        }

        return byAlias.TryGetValue(hint, out var byAliasMatch) ? byAliasMatch : null;
    }

    private static (int? PageNumber, int? OffsetInPage) ResolvePage(IReadOnlyList<TextPage>? pages, int position)
    {
        if (pages is null || pages.Count == 0)
        {
            return (null, null);
        }

        // The page whose range contains the position; fall back to the last page that starts at or
        // before it (a span may straddle a page boundary — it is stamped with the page it starts on).
        TextPage? page = null;
        foreach (var candidate in pages)
        {
            if (candidate.StartOffset > position)
            {
                break;
            }

            page = candidate;
        }

        return page is null ? (null, null) : (page.PageNumber, position - page.StartOffset);
    }
}
