using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IGraphAdminService
{
    Task<Result> ValidateGraphAsync(CancellationToken cancellationToken = default);

    Task<Result> RebuildGraphAsync(CancellationToken cancellationToken = default);

    Task<Result> RepairGraphAsync(CancellationToken cancellationToken = default);

    Task<Result> MergeDuplicateEntitiesAsync(CancellationToken cancellationToken = default);

    Task<Result> RecomputeCommunitiesAsync(CancellationToken cancellationToken = default);

    Task<Result> RegenerateSummariesAsync(CancellationToken cancellationToken = default);

    Task<Result> OptimizeGraphAsync(CancellationToken cancellationToken = default);
}
