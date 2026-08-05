using System.Collections.Concurrent;

namespace Aletheia.Repository.API.Services;

/// <summary>Runtime counters for ingestion diagnostics surfaced by GET /api/rags/status.</summary>
public interface IIngestionDiagnostics
{
    void RecordTemplateGateSkip(string sourceName);

    void RecordExtractionFailure(string sourceName, string error);

    long TemplateGateSkipCount { get; }

    long ExtractionFailureCount { get; }

    IReadOnlyList<string> TemplateGateSkips { get; }
}

public sealed class IngestionDiagnostics : IIngestionDiagnostics
{
    private const int MaxRecent = 50;

    private long _templateGateSkips;
    private long _extractionFailures;
    private readonly ConcurrentQueue<string> _recentTemplateGateSkips = new();

    public long TemplateGateSkipCount => Interlocked.Read(ref _templateGateSkips);

    public long ExtractionFailureCount => Interlocked.Read(ref _extractionFailures);

    public IReadOnlyList<string> TemplateGateSkips => _recentTemplateGateSkips.ToArray();

    public void RecordTemplateGateSkip(string sourceName)
    {
        Interlocked.Increment(ref _templateGateSkips);
        EnqueueRecent(_recentTemplateGateSkips, sourceName);
    }

    public void RecordExtractionFailure(string sourceName, string error)
    {
        Interlocked.Increment(ref _extractionFailures);
        EnqueueRecent(_recentTemplateGateSkips, $"{sourceName} ({error})");
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
