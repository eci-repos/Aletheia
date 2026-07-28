using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Application.Ranking;

internal sealed class RetrievalCandidate
{
    public RetrievalCandidate(SearchResult result, string strategy, float graphScore = 0f, float contextScore = 0f, float citationScore = 0f)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
        Strategy = string.IsNullOrWhiteSpace(strategy) ? "semantic" : strategy;
        GraphScore = Clamp(graphScore);
        ContextScore = Clamp(contextScore);
        CitationScore = Clamp(citationScore);
    }

    public SearchResult Result { get; }

    public string Strategy { get; }

    public float GraphScore { get; }

    public float ContextScore { get; }

    public float CitationScore { get; }

    public float SemanticScore => Clamp(Result.Score);

    public RetrievalCandidate Merge(RetrievalCandidate other)
    {
        return new RetrievalCandidate(
            Result.Score >= other.Result.Score ? Result : other.Result,
            MergeStrategy(Strategy, other.Strategy),
            Math.Max(GraphScore, other.GraphScore),
            Math.Max(ContextScore, other.ContextScore),
            Math.Max(CitationScore, other.CitationScore));
    }

    private static string MergeStrategy(string first, string second)
    {
        if (first.Equals(second, StringComparison.OrdinalIgnoreCase))
        {
            return first;
        }

        var parts = first.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(second.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join("+", parts);
    }

    private static float Clamp(float value) => Math.Clamp(value, 0f, 1f);
}

internal static class GraphRagResultRanker
{
    public static async Task<IReadOnlyList<SearchResult>> RankAndCiteAsync(
        IEnumerable<RetrievalCandidate> candidates,
        ICitationPathService citationPath,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (citationPath is null)
        {
            throw new ArgumentNullException(nameof(citationPath));
        }

        var merged = new Dictionary<Guid, RetrievalCandidate>();
        foreach (var candidate in candidates)
        {
            var chunkId = candidate.Result.Chunk.Id;
            merged[chunkId] = merged.TryGetValue(chunkId, out var existing)
                ? existing.Merge(candidate)
                : candidate;
        }

        var ranked = new List<(RetrievalCandidate Candidate, float FinalScore, IReadOnlyList<string> Citations)>();

        foreach (var candidate in merged.Values)
        {
            var citations = await ResolveCitationsAsync(candidate, citationPath, cancellationToken).ConfigureAwait(false);
            var citationScore = citations.Count > 0 ? 1f : candidate.CitationScore;
            var finalScore = ComputeFinalScore(candidate, citationScore);
            ranked.Add((candidate, finalScore, citations));
        }

        return ranked
            .OrderByDescending(r => r.FinalScore)
            .ThenByDescending(r => r.Candidate.SemanticScore)
            .Take(limit)
            .Select((r, index) => new SearchResult(
                r.Candidate.Result.Chunk,
                r.FinalScore,
                r.Citations,
                new Dictionary<string, float>
                {
                    ["semantic"] = r.Candidate.SemanticScore,
                    ["graph"] = r.Candidate.GraphScore,
                    ["context"] = r.Candidate.ContextScore,
                    ["citation"] = r.Citations.Count > 0 ? 1f : r.Candidate.CitationScore,
                    ["final"] = r.FinalScore
                },
                r.Candidate.Strategy,
                index + 1))
            .ToList();
    }

    private static async Task<IReadOnlyList<string>> ResolveCitationsAsync(
        RetrievalCandidate candidate,
        ICitationPathService citationPath,
        CancellationToken cancellationToken)
    {
        var citations = new HashSet<string>(candidate.Result.Citations, StringComparer.OrdinalIgnoreCase);

        var sourceResult = await citationPath.GetDocumentSourcesAsync(candidate.Result.Chunk.SourceId.ToString(), cancellationToken).ConfigureAwait(false);
        AddCitations(citations, sourceResult);

        if (citations.Count == 0)
        {
            citations.Add(candidate.Result.Chunk.SourceId.ToString());
        }

        return citations.ToList();
    }

    private static void AddCitations(HashSet<string> citations, Result<IReadOnlyList<string>> result)
    {
        if (result.IsSuccess && result.Value is not null)
        {
            foreach (var source in result.Value.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                citations.Add(source);
            }
        }
    }

    private static float ComputeFinalScore(RetrievalCandidate candidate, float citationScore)
    {
        var final = (candidate.SemanticScore * 0.62f)
            + (candidate.GraphScore * 0.2f)
            + (candidate.ContextScore * 0.1f)
            + (Math.Clamp(citationScore, 0f, 1f) * 0.08f);

        return Math.Clamp(final, 0f, 1f);
    }
}
