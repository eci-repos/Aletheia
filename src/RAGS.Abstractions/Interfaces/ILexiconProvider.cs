using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>
/// Cached access to the lexicon for the hot retrieval path. Loads concepts from the repository on
/// first use and caches them; <c>Invalidate</c> clears the cache so admin edits take effect.
/// </summary>
public interface ILexiconProvider
{
    Task<IReadOnlyList<LexiconConcept>> GetConceptsAsync(CancellationToken cancellationToken = default);

    void Invalidate();
}
