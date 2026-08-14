using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace Aletheia.RAGS.Application.Lexicon;

/// <summary>
/// Orchestrates grounded fact extraction at ingestion: propose (LLM) → verify (fidelity gate) →
/// normalize (lexicon) → persist. Verified facts are written to the repository; concept hints that
/// match no known concept are recorded as unmapped terms for the governance loop. Failures are
/// best-effort and never block ingestion.
/// </summary>
public sealed class GroundedFactExtractionService : IFactExtractionService
{
    private readonly ILexiconRepository _lexiconRepository;
    private readonly IFactProposer _proposer;
    private readonly ILogger<GroundedFactExtractionService> _logger;

    public GroundedFactExtractionService(
        ILexiconRepository lexiconRepository,
        IFactProposer proposer,
        ILogger<GroundedFactExtractionService>? logger = null)
    {
        _lexiconRepository = lexiconRepository ?? throw new ArgumentNullException(nameof(lexiconRepository));
        _proposer = proposer ?? throw new ArgumentNullException(nameof(proposer));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GroundedFactExtractionService>.Instance;
    }

    public async Task<Result<IReadOnlyList<DocumentFact>>> ExtractAsync(
        Guid sourceId,
        string text,
        IReadOnlyList<TextPage>? pages,
        string? templateName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var conceptsResult = await _lexiconRepository.GetAllConceptsAsync(cancellationToken).ConfigureAwait(false);
            if (conceptsResult.IsFailure)
            {
                return Result<IReadOnlyList<DocumentFact>>.Failure(conceptsResult.Error ?? "Lexicon load failed.");
            }

            var concepts = conceptsResult.Value ?? Array.Empty<LexiconConcept>();
            // Sprint 71: template_scope enforcement — scoped concepts apply only to documents of that
            // template. The same filtered set feeds the verifier and the unmapped-term recorder so a
            // scoped concept's aliases never suppress unmapped hints in a document of another template.
            var applicable = concepts.Where(c => FactVerifier.IsApplicable(c, templateName)).ToList();
            var proposalsResult = await _proposer.ProposeAsync(text, applicable, cancellationToken).ConfigureAwait(false);
            if (proposalsResult.IsFailure)
            {
                return Result<IReadOnlyList<DocumentFact>>.Failure(proposalsResult.Error ?? "Fact proposal failed.");
            }

            var proposals = proposalsResult.Value ?? Array.Empty<ProposedFact>();
            var facts = FactVerifier.Verify(proposals, text, pages, applicable, templateName);
            foreach (var fact in facts)
            {
                fact.SourceId = sourceId;
            }

            if (facts.Count > 0)
            {
                var saveResult = await _lexiconRepository.SaveFactsAsync(sourceId, facts, cancellationToken).ConfigureAwait(false);
                if (saveResult.IsFailure)
                {
                    _logger.LogWarning("Fact persistence failed for {SourceId}: {Error}.", sourceId, saveResult.Error);
                }
            }

            await RecordUnmappedTermsAsync(sourceId, proposals, applicable, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Grounded fact extraction for {SourceId}: {Proposed} proposed, {Verified} verified.",
                sourceId,
                proposals.Count,
                facts.Count);
            return Result<IReadOnlyList<DocumentFact>>.Success(facts);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<DocumentFact>>.Failure($"Grounded fact extraction failed. {ex.Message}");
        }
    }

    private async Task RecordUnmappedTermsAsync(
        Guid sourceId,
        IReadOnlyList<ProposedFact> proposals,
        IReadOnlyList<LexiconConcept> concepts,
        CancellationToken cancellationToken)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var concept in concepts)
        {
            known.Add(concept.Key);
            foreach (var alias in concept.Aliases ?? Array.Empty<string>())
            {
                known.Add(alias);
            }
        }

        foreach (var hint in proposals
            .Select(p => p.ConceptHint)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (known.Contains(hint))
            {
                continue;
            }

            try
            {
                await _lexiconRepository.RecordUnmappedTermAsync(hint, sourceId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to record unmapped term '{Term}' for {SourceId}.", hint, sourceId);
            }
        }
    }
}
