namespace Aletheia.RAGS.Abstractions.Configuration;

public sealed class RetrievalOptions
{
    public const string SectionName = "RAGS";

    /// <summary>Minimum cosine similarity (0..1) for vector results. When the best vector result is below this floor
    /// (or the vector search returns nothing), retrieval falls back to keyword search over chunk content and file names.
    /// Default 0 preserves the current behavior (fallback only when vector results are empty).</summary>
    public double MinimumScore { get; set; }
}
