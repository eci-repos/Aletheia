using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;

namespace Aletheia.RAGS.Application.Graph;

public sealed class GraphAdminService : IGraphAdminService
{
    private readonly IGraphProvider _provider;
    private readonly ICommunityDetectionService _communityDetection;
    private readonly IGraphSummaryService _summaryService;

    public GraphAdminService(
        IGraphProvider provider,
        ICommunityDetectionService communityDetection,
        IGraphSummaryService summaryService)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _communityDetection = communityDetection ?? throw new ArgumentNullException(nameof(communityDetection));
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
    }

    public async Task<Result> ValidateGraphAsync(CancellationToken cancellationToken = default)
    {
        var exists = await _provider.GraphExistsAsync(cancellationToken).ConfigureAwait(false);
        return exists.IsSuccess && exists.Value
            ? Result.Success()
            : Result.Failure("Graph validation failed or graph does not exist.");
    }

    public Task<Result> RebuildGraphAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Implement full rebuild with entity extraction and relationship discovery
        return _provider.ClearAsync(cancellationToken);
    }

    public Task<Result> RepairGraphAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Implement graph repair (fix dangling edges, orphan nodes)
        return Task.FromResult(Result.Success());
    }

    public Task<Result> MergeDuplicateEntitiesAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Implement duplicate entity detection and merge
        return Task.FromResult(Result.Success());
    }

    public async Task<Result> RecomputeCommunitiesAsync(CancellationToken cancellationToken = default)
    {
        var communities = await _communityDetection.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (communities.IsFailure || communities.Value is null)
        {
            return Result.Failure("Community detection failed.");
        }

        // Persist community assignments to nodes
        foreach (var community in communities.Value)
        {
            foreach (var memberId in community.MemberIds)
            {
                await _communityDetection.AssignAsync(memberId, community.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        return Result.Success();
    }

    public async Task<Result> RegenerateSummariesAsync(CancellationToken cancellationToken = default)
    {
        var nodes = await _provider.GetNodesAsync(cancellationToken).ConfigureAwait(false);
        if (nodes.IsSuccess && nodes.Value is not null)
        {
            var entityNodes = nodes.Value.Where(n => n.Type != "Source").ToList();

            // Regenerate entity summaries (fire-and-forget; collect failures)
            var failures = new List<string>();
            foreach (var entity in entityNodes.Take(100)) // Limit to prevent overload
            {
                var result = await _summaryService.SummarizeEntityAsync(entity.Id, cancellationToken).ConfigureAwait(false);
                if (result.IsFailure)
                {
                    failures.Add($"{entity.Id}: {result.Error}");
                }
            }

            if (failures.Any())
            {
                return Result.Failure($"Summary regeneration completed with {failures.Count} failures.");
            }
        }

        return Result.Success();
    }

    public Task<Result> OptimizeGraphAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Implement index optimization and compaction
        return Task.FromResult(Result.Success());
    }
}
