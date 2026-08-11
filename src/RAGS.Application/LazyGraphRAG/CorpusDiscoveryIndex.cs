using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Aletheia.RAGS.Application.LazyGraphRAG;

/// <summary>
/// Lightweight corpus index for LazyGraphRAG.
/// Computes TF-IDF, BM25, and text statistics without LLM calls.
/// The in-memory index is the hot path; when an <see cref="ICorpusIndexRepository"/> is supplied it
/// is loaded at startup and persisted write-through so the corpus survives restart and multi-instance.
/// </summary>
public sealed class CorpusDiscoveryIndex : ICorpusDiscoveryIndex
{
    private readonly Dictionary<Guid, DocumentIndex> _indices = new();
    private readonly Dictionary<string, int> _documentFrequency = new(StringComparer.OrdinalIgnoreCase);
    private int _totalDocuments;

    private readonly ICorpusIndexRepository? _repository;
    private readonly ILogger<CorpusDiscoveryIndex>? _logger;

    public CorpusDiscoveryIndex(
        ICorpusIndexRepository? repository = null,
        ILogger<CorpusDiscoveryIndex>? logger = null)
    {
        _repository = repository;
        _logger = logger;

        if (_repository is not null)
        {
            LoadPersistedCorpus();
        }
    }

    public async Task<Result> IndexAsync(string content, Guid sourceId, CancellationToken cancellationToken = default)
    {
        var terms = Tokenize(content);
        var termFrequency = ComputeTermFrequency(terms);

        _indices[sourceId] = new DocumentIndex
        {
            SourceId = sourceId,
            Terms = terms,
            TermFrequency = termFrequency,
            DocumentLength = terms.Count,
        };

        // Update global document frequency
        foreach (var uniqueTerm in termFrequency.Keys)
        {
            if (!_documentFrequency.ContainsKey(uniqueTerm))
                _documentFrequency[uniqueTerm] = 0;
            _documentFrequency[uniqueTerm]++;
        }

        _totalDocuments = _indices.Count;

        // Recompute IDF and BM25 for all documents if corpus grew significantly
        RecomputeCorpusStats();

        // Write-through persistence is best-effort: the in-memory index stays authoritative, so a
        // transient DB failure must never fail ingestion or degrade the hot path.
        if (_repository is not null)
        {
            try
            {
                var persistResult = await _repository
                    .UpsertDocumentAsync(sourceId, termFrequency, terms.Count, cancellationToken)
                    .ConfigureAwait(false);
                if (persistResult.IsFailure)
                {
                    _logger?.LogWarning(
                        "Failed to persist LazyGraphRAG corpus index for source {SourceId}: {Error}",
                        sourceId,
                        persistResult.Error);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Failed to persist LazyGraphRAG corpus index for source {SourceId}; in-memory index remains authoritative.",
                    sourceId);
            }
        }

        return Result.Success();
    }

    public IReadOnlyList<string> GetTerms(Guid sourceId)
    {
        return _indices.TryGetValue(sourceId, out var index)
            ? index.Terms.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : Array.Empty<string>();
    }

    public float GetTfIdf(string term, Guid sourceId)
    {
        if (!_indices.TryGetValue(sourceId, out var index))
            return 0f;

        var tf = index.TermFrequency.GetValueOrDefault(term, 0) / (float)index.DocumentLength;
        var idf = ComputeIdf(term);
        return tf * idf;
    }

    public float GetBm25Score(string term, Guid sourceId)
    {
        if (!_indices.TryGetValue(sourceId, out var index))
            return 0f;

        const float k1 = 1.5f;
        const float b = 0.75f;

        var tf = index.TermFrequency.GetValueOrDefault(term, 0);
        var idf = ComputeIdf(term);
        var avgDocLength = GetAverageDocumentLength();

        var numerator = tf * (k1 + 1);
        var denominator = tf + k1 * (1 - b + b * (index.DocumentLength / avgDocLength));

        return idf * (numerator / (denominator + float.Epsilon));
    }

    public CorpusStatistics GetStatistics(Guid sourceId)
    {
        if (!_indices.TryGetValue(sourceId, out var index))
            return new CorpusStatistics();

        return new CorpusStatistics
        {
            TotalTerms = index.Terms.Count,
            UniqueTerms = index.TermFrequency.Count,
            DocumentLength = index.DocumentLength,
            AverageDocumentLength = GetAverageDocumentLength(),
        };
    }

    public IReadOnlyList<Guid> SearchCorpus(string query, int topK = 10)
    {
        var queryTerms = Tokenize(query).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var scores = new Dictionary<Guid, float>();

        foreach (var kvp in _indices)
        {
            var sourceId = kvp.Key;
            var score = 0f;
            foreach (var term in queryTerms)
            {
                score += GetBm25Score(term, sourceId);
            }
            if (score > 0)
                scores[sourceId] = score;
        }

        return scores
            .OrderByDescending(s => s.Value)
            .Take(topK)
            .Select(s => s.Key)
            .ToList();
    }

    private void LoadPersistedCorpus()
    {
        try
        {
            var loadResult = _repository!.LoadAsync().GetAwaiter().GetResult();
            if (loadResult.IsFailure || loadResult.Value is null)
            {
                _logger?.LogWarning(
                    "Failed to load persisted LazyGraphRAG corpus index: {Error}",
                    loadResult.Error);
                return;
            }

            foreach (var document in loadResult.Value.Documents)
            {
                var termFrequency = new Dictionary<string, int>(document.TermFrequency, StringComparer.OrdinalIgnoreCase);
                _indices[document.SourceId] = new DocumentIndex
                {
                    SourceId = document.SourceId,
                    Terms = termFrequency
                        .SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value))
                        .ToList(),
                    TermFrequency = termFrequency,
                    DocumentLength = document.DocumentLength,
                };

                foreach (var term in termFrequency.Keys)
                {
                    _documentFrequency[term] = _documentFrequency.GetValueOrDefault(term, 0) + 1;
                }
            }

            _totalDocuments = _indices.Count;
            RecomputeCorpusStats();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Failed to load persisted LazyGraphRAG corpus index; starting with an empty in-memory index.");
        }
    }

    private static List<string> Tokenize(string text)
    {
        var cleaned = Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9\s]", " ");
        var words = cleaned.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
            "and", "or", "but", "of", "in", "on", "at", "to", "for", "with", "by",
            "this", "that", "these", "those", "it", "its", "from", "as", "has", "have"
        };

        return words
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .ToList();
    }

    private static Dictionary<string, int> ComputeTermFrequency(List<string> terms)
    {
        var tf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
        {
            if (!tf.ContainsKey(term))
                tf[term] = 0;
            tf[term]++;
        }
        return tf;
    }

    private float ComputeIdf(string term)
    {
        var docFreq = _documentFrequency.GetValueOrDefault(term, 0);
        if (docFreq == 0)
            return 0f;
        return MathF.Log((_totalDocuments + 1f) / (docFreq + 0.5f));
    }

    private float GetAverageDocumentLength()
    {
        if (_indices.Count == 0)
            return 1f;
        return (float)_indices.Values.Average(i => i.DocumentLength);
    }

    private void RecomputeCorpusStats()
    {
        foreach (var index in _indices.Values)
        {
            index.AverageDocumentLength = GetAverageDocumentLength();
        }
    }

    private sealed class DocumentIndex
    {
        public Guid SourceId { get; set; }
        public List<string> Terms { get; set; } = new();
        public Dictionary<string, int> TermFrequency { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public int DocumentLength { get; set; }
        public float AverageDocumentLength { get; set; }
    }
}
