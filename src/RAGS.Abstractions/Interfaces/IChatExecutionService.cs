using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IChatExecutionService
{
    Task<Result<ChatJobSnapshot>> StartAsync(Guid planId, CancellationToken cancellationToken = default);

    Task<Result> CancelAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<Result<ChatJobSnapshot?>> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default);

    IReadOnlyList<ChatJobSnapshot> List(int take = 50);

    Task<Result<ChatProgressRecord?>> GetProgressAsync(Guid jobId, CancellationToken cancellationToken = default);
}
