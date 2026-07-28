using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IGraphAnalyticsService
{
    Task<Result<IReadOnlyList<string>>> DetectCommunitiesAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyDictionary<string, double>>> ComputeCentralityAsync(CancellationToken cancellationToken = default);

    Task<Result<GraphMetrics>> ComputeGraphMetricsAsync(CancellationToken cancellationToken = default);

    Task<Result<GraphHealth>> ComputeGraphHealthAsync(CancellationToken cancellationToken = default);
}

public sealed class GraphMetrics
{
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }
    public double Density { get; set; }
    public double AverageDegree { get; set; }
    public int ConnectedComponents { get; set; }
}

public sealed class GraphHealth
{
    public bool IsConsistent { get; set; }
    public int OrphanNodes { get; set; }
    public int DanglingEdges { get; set; }
    public IReadOnlyList<string> Issues { get; set; } = Array.Empty<string>();
}
