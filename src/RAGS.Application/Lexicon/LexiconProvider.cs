using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Application.Lexicon;

/// <summary>
/// Cached access to the lexicon for the hot retrieval path. Loads concepts from the repository on
/// first use and caches them in memory; <c>Invalidate</c> clears the cache so admin edits take
/// effect on the next read. A failed load is not cached, so a transient DB error does not poison
/// the cache.
/// </summary>
public sealed class LexiconProvider : ILexiconProvider
{
    private readonly ILexiconRepository _repository;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<LexiconConcept>? _cache;

    public LexiconProvider(ILexiconRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<LexiconConcept>> GetConceptsAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache is not null)
            {
                return _cache;
            }

            var result = await _repository.GetAllConceptsAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                _cache = result.Value ?? Array.Empty<LexiconConcept>();
            }

            return _cache ?? Array.Empty<LexiconConcept>();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _cache = null;
}
