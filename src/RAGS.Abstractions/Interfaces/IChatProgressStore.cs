using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IChatProgressStore
{
    Task<Result> SaveAsync(ChatProgressRecord progress, CancellationToken cancellationToken = default);

    Task<Result<ChatProgressRecord?>> GetAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<Result> AppendHeartbeatAsync(
        Guid jobId,
        ChatProgressHeartbeat heartbeat,
        CancellationToken cancellationToken = default);

    Task<Result> AppendMessageAsync(
        Guid jobId,
        ChatProgressMessage message,
        CancellationToken cancellationToken = default);

    Task<Result> UpdateStepAsync(
        Guid jobId,
        ChatProgressStep step,
        CancellationToken cancellationToken = default);

    Task<Result> SetPartialResultAsync(
        Guid jobId,
        string? partialResult,
        CancellationToken cancellationToken = default);

    Task<Result> FinalizeAsync(
        Guid jobId,
        ChatJobStatus status,
        string? result,
        string? error,
        CancellationToken cancellationToken = default);

    Task<Result> SetTelemetryAsync(
        Guid jobId,
        ChatExecutionTelemetry telemetry,
        CancellationToken cancellationToken = default);
}
