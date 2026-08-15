namespace Aletheia.RAGS.Abstractions.Models;

/// <summary>
/// Snapshot of knowledge-graph summary coverage. Lets an operator see whether summaries exist
/// and how much of the graph is summarized — the "when do summaries get created" question,
/// answered from the graph itself rather than from job history.
/// </summary>
public sealed class SummariesStatusSnapshot
{
    public bool GraphExists { get; init; }
    public int NodeCount { get; init; }
    public int EntityCount { get; init; }
    public int CommunityCount { get; init; }
    public int SummarizedCommunityCount { get; init; }
    public int SourceCount { get; init; }
    public IReadOnlyList<SourceSummaryStatus> Sources { get; init; } = Array.Empty<SourceSummaryStatus>();
}

/// <summary>Per-source summary coverage. A community counts toward every source it touches.</summary>
public sealed class SourceSummaryStatus
{
    public Guid SourceId { get; init; }
    public string SourceName { get; init; } = string.Empty;
    public int EntityCount { get; set; }
    public int CommunityCount { get; set; }
    public int SummarizedCommunityCount { get; set; }
}
