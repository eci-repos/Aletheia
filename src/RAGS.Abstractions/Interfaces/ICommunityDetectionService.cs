using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface ICommunityDetectionService
{
    Task<Result<IReadOnlyList<GraphCommunity>>> DiscoverAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphCommunity>>> DetectClustersAsync(CancellationToken cancellationToken = default);

    Task<Result> AssignAsync(string nodeId, string communityId, CancellationToken cancellationToken = default);

    Task<Result<GraphCommunity?>> GetCommunityAsync(string communityId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GraphCommunity>>> GetCommunitiesForNodeAsync(string nodeId, CancellationToken cancellationToken = default);
}

public sealed class GraphCommunity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IReadOnlyList<string> MemberIds { get; set; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
}
