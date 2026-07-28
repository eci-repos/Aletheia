using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using System.Text.RegularExpressions;

namespace Aletheia.RAGS.Application.LazyGraphRAG;

/// <summary>
/// Lightweight corpus index for LazyGraphRAG.
/// Computes TF-IDF, BM25, and text statistics without LLM calls.
/// </summary>
public sealed class CorpusDiscoveryIndex : ICorpusDiscoveryIndex
{
    private readonly Dictionary<Guid, DocumentIndex> _indices = new();
    private readonly Dictionary<string, int> _documentFrequency = new(StringComparer.OrdinalIgnoreCase);
    private int _totalDocuments;

    public Task<Result> IndexAsync(string content, Guid sourceId, CancellationToken cancellationToken = default)
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

        return Task.FromResult(Result.Success());
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
