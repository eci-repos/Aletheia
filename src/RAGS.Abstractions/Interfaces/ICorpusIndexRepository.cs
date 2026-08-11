using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>
/// Persistence for the LazyGraphRAG corpus index (term frequency / doc frequency / avg doc length).
/// The in-memory <see cref="ICorpusDiscoveryIndex"/> remains the hot path; this repository is a
/// write-through / load-on-start store so the corpus survives restart and multi-instance.
/// </summary>
public interface ICorpusIndexRepository
{
    /// <summary>Upserts one document's term-frequency map and length (replaces the previous entry).</summary>
    Task<Result> UpsertDocumentAsync(
        Guid sourceId,
        IReadOnlyDictionary<string, int> termFrequency,
        int documentLength,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the full persisted corpus (all documents + their term frequencies).</summary>
    Task<Result<CorpusIndexSnapshot>> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Full persisted corpus snapshot returned by <see cref="ICorpusIndexRepository.LoadAsync"/>.</summary>
public sealed class CorpusIndexSnapshot
{
    public IReadOnlyList<CorpusDocumentIndex> Documents { get; set; } = Array.Empty<CorpusDocumentIndex>();
}

/// <summary>One persisted document's corpus statistics.</summary>
public sealed class CorpusDocumentIndex
{
    public Guid SourceId { get; set; }

    public int DocumentLength { get; set; }

    public IReadOnlyDictionary<string, int> TermFrequency { get; set; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}
