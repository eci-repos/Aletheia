using System.Collections.Concurrent;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Application.Planning;

public sealed class InMemoryChatProgressStore : IChatProgressStore
{
    private readonly ConcurrentDictionary<Guid, ChatProgressRecord> _records = new();

    public Task<Result> SaveAsync(ChatProgressRecord progress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (progress is null)
        {
            throw new ArgumentNullException(nameof(progress));
        }

        _records[progress.JobId] = progress;
        return Task.FromResult(Result.Success());
    }

    public Task<Result<ChatProgressRecord?>> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _records.TryGetValue(jobId, out var record);
        return Task.FromResult(Result<ChatProgressRecord?>.Success(record));
    }

    public Task<Result> AppendHeartbeatAsync(
        Guid jobId,
        ChatProgressHeartbeat heartbeat,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return MutateAsync(jobId, record => record with
        {
            Heartbeats = record.Heartbeats.Append(heartbeat).ToList()
        }, cancellationToken);
    }

    public Task<Result> AppendMessageAsync(
        Guid jobId,
        ChatProgressMessage message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return MutateAsync(jobId, record => record with
        {
            Messages = record.Messages.Append(message).ToList()
        }, cancellationToken);
    }

    public Task<Result> UpdateStepAsync(
        Guid jobId,
        ChatProgressStep step,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return MutateAsync(jobId, record =>
        {
            var steps = record.Steps.ToList();
            var existingIndex = steps.FindIndex(s => s.Name.Equals(step.Name, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                steps[existingIndex] = step;
                steps.Sort((a, b) => a.Order.CompareTo(b.Order));
            }
            else
            {
                steps.Add(step);
                steps.Sort((a, b) => a.Order.CompareTo(b.Order));
            }

            return record with { Steps = steps };
        }, cancellationToken);
    }

    public Task<Result> SetPartialResultAsync(
        Guid jobId,
        string? partialResult,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return MutateAsync(jobId, record => record with { PartialResult = partialResult }, cancellationToken);
    }

    public Task<Result> FinalizeAsync(
        Guid jobId,
        ChatJobStatus status,
        string? result,
        string? error,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return MutateAsync(jobId, record =>
        {
            var now = DateTimeOffset.UtcNow;
            var steps = record.Steps.ToList();

            if (status is ChatJobStatus.Failed or ChatJobStatus.Cancelled)
            {
                for (var i = 0; i < steps.Count; i++)
                {
                    var step = steps[i];
                    if (step.Status == ChatProgressStepStatus.Running)
                    {
                        steps[i] = new ChatProgressStep
                        {
                            Name = step.Name,
                            Status = ChatProgressStepStatus.Failed,
                            Order = step.Order,
                            StartedAt = step.StartedAt,
                            CompletedAt = now,
                            Detail = error ?? $"{step.Name} terminated because the job finalized with status {status}."
                        };
                    }
                }
            }

            return record with
            {
                Status = status,
                FinalResult = result,
                Error = error,
                CompletedAt = now,
                Steps = steps
            };
        }, cancellationToken);
    }

    public Task<Result> SetTelemetryAsync(
        Guid jobId,
        ChatExecutionTelemetry telemetry,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return MutateAsync(jobId, record => record with { Telemetry = telemetry }, cancellationToken);
    }

    private Task<Result> MutateAsync(Guid jobId, Func<ChatProgressRecord, ChatProgressRecord> mutation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_records.TryGetValue(jobId, out var existing))
        {
            return Task.FromResult(Result.Failure("Progress record not found."));
        }

        var updated = mutation(existing);
        _records[jobId] = updated;
        return Task.FromResult(Result.Success());
    }
}
