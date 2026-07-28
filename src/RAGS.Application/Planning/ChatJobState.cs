using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Application.Planning;

internal sealed class ChatJobState
{
    private readonly object _gate = new();

    public ChatJobState(Guid jobId, Guid planId, string prompt)
    {
        JobId = jobId;
        PlanId = planId;
        Prompt = prompt;
        CreatedAt = DateTimeOffset.UtcNow;
        LastHeartbeatAt = CreatedAt;
    }

    public Guid JobId { get; }
    public Guid PlanId { get; }
    public string Prompt { get; }
    public DateTimeOffset CreatedAt { get; }

    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return Status is ChatJobStatus.Queued or ChatJobStatus.Running;
            }
        }
    }

    public bool IsCancelled
    {
        get
        {
            lock (_gate)
            {
                return Status == ChatJobStatus.Cancelled;
            }
        }
    }

    public bool IsTerminal
    {
        get
        {
            lock (_gate)
            {
                return Status is ChatJobStatus.Succeeded or ChatJobStatus.Failed or ChatJobStatus.Cancelled;
            }
        }
    }

    public int MissedHeartbeatCount { get; private set; }

    public DateTimeOffset LastHeartbeatAt { get; private set; }

    private ChatJobStatus Status { get; set; } = ChatJobStatus.Queued;
    private string Stage { get; set; } = "Queued";
    public int PercentComplete { get; private set; }
    private string Detail { get; set; } = "Waiting for the execution worker.";
    private DateTimeOffset? StartedAt { get; set; }
    private DateTimeOffset? CompletedAt { get; set; }
    private string? Result { get; set; }
    private string? Error { get; set; }

    public void Start(string stage, string detail)
    {
        lock (_gate)
        {
            StartedAt = DateTimeOffset.UtcNow;
            Status = ChatJobStatus.Running;
            Apply(stage, detail, 5);
        }
    }

    public void Update(string stage, string detail, int? percentComplete = null, bool force = false)
    {
        lock (_gate)
        {
            if (Status != ChatJobStatus.Running)
            {
                return;
            }

            Apply(stage, detail, percentComplete ?? PercentComplete);
        }
    }

    public void Succeed(string result)
    {
        lock (_gate)
        {
            Status = ChatJobStatus.Succeeded;
            Apply("Completed", "Chat execution completed.", 100);
            Result = result;
            CompletedAt = DateTimeOffset.UtcNow;
            Error = null;
        }
    }

    public void Fail(string error)
    {
        lock (_gate)
        {
            Status = ChatJobStatus.Failed;
            Apply("Failed", error, Math.Max(PercentComplete, 1));
            Error = error;
            CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    public void Cancel(string detail)
    {
        lock (_gate)
        {
            if (Status is ChatJobStatus.Succeeded or ChatJobStatus.Failed or ChatJobStatus.Cancelled)
            {
                return;
            }

            Status = ChatJobStatus.Cancelled;
            Apply("Cancelled", detail, PercentComplete);
            Error = detail;
            CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    public void RecordHeartbeat()
    {
        lock (_gate)
        {
            LastHeartbeatAt = DateTimeOffset.UtcNow;
            MissedHeartbeatCount = 0;
        }
    }

    public int IncrementMissedHeartbeat()
    {
        lock (_gate)
        {
            MissedHeartbeatCount++;
            return MissedHeartbeatCount;
        }
    }

    public ChatJobSnapshot ToSnapshot()
    {
        lock (_gate)
        {
            return new ChatJobSnapshot
            {
                JobId = JobId,
                PlanId = PlanId,
                Prompt = Prompt,
                Status = Status,
                Stage = Stage,
                PercentComplete = PercentComplete,
                Detail = Detail,
                CreatedAt = CreatedAt,
                StartedAt = StartedAt,
                LastHeartbeatAt = LastHeartbeatAt,
                CompletedAt = CompletedAt,
                Result = Result,
                Error = Error
            };
        }
    }

    private void Apply(string stage, string detail, int percentComplete)
    {
        Stage = string.IsNullOrWhiteSpace(stage) ? Stage : stage;
        Detail = string.IsNullOrWhiteSpace(detail) ? Detail : detail;
        PercentComplete = Math.Clamp(percentComplete, 0, 100);
        LastHeartbeatAt = DateTimeOffset.UtcNow;
        MissedHeartbeatCount = 0;
    }
}
