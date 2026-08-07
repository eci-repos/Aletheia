using System.Collections.Concurrent;

namespace Aletheia.Repository.API.Services;

/// <summary>Runtime counters for ingestion diagnostics surfaced by GET /api/rags/status.</summary>
public interface IIngestionDiagnostics
{
    /// <summary>Records a document ingested with no matching canonical template (Sprint 59 softened gate).</summary>
    void RecordUncategorizedIngest(string sourceName);

    void RecordExtractionFailure(string sourceName, string error);

    long UncategorizedIngestCount { get; }

    long ExtractionFailureCount { get; }

    IReadOnlyList<string> UncategorizedIngests { get; }
}

public sealed class IngestionDiagnostics : IIngestionDiagnostics
{
    private const int MaxRecent = 50;

    private long _uncategorizedIngests;
    private long _extractionFailures;
    private readonly ConcurrentQueue<string> _recentUncategorizedIngests = new();

    public long UncategorizedIngestCount => Interlocked.Read(ref _uncategorizedIngests);

    public long ExtractionFailureCount => Interlocked.Read(ref _extractionFailures);

    public IReadOnlyList<string> UncategorizedIngests => _recentUncategorizedIngests.ToArray();

    public void RecordUncategorizedIngest(string sourceName)
    {
        Interlocked.Increment(ref _uncategorizedIngests);
        EnqueueRecent(_recentUncategorizedIngests, sourceName);
    }

    public void RecordExtractionFailure(string sourceName, string error)
    {
        Interlocked.Increment(ref _extractionFailures);
        EnqueueRecent(_recentUncategorizedIngests, $"{sourceName} ({error})");
    }

    private static void EnqueueRecent(ConcurrentQueue<string> queue, string value)
    {
        queue.Enqueue(value);
        while (queue.Count > MaxRecent)
        {
            queue.TryDequeue(out _);
        }
    }
}
