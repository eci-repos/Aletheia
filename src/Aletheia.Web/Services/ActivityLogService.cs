namespace Aletheia.Web.Services;

public sealed class ActivityLogService
{
    private const int MaxEntries = 300;
    private readonly List<ActivityLogEntry> _entries = new();
    private readonly Dictionary<Guid, BackgroundJobClientSnapshot> _jobs = new();
    private readonly Dictionary<Guid, string> _lastJobLogKeys = new();

    public event Action? Changed;

    public IReadOnlyList<ActivityLogEntry> Entries => _entries;

    public IReadOnlyList<BackgroundJobClientSnapshot> Jobs => _jobs.Values
        .OrderByDescending(job => job.CreatedAt)
        .ToList();

    public int RunningCount => ActiveJobCount + _entries
        .GroupBy(entry => entry.ActivityId)
        .Count(group => group.All(entry => entry.Status is ActivityLogStatus.Running or ActivityLogStatus.Info));

    public int ActiveJobCount => _jobs.Values.Count(job => job.Status is "Queued" or "Running");

    public Guid Begin(string area, string title, string message)
    {
        var activityId = Guid.NewGuid();
        Add(activityId, area, title, message, ActivityLogStatus.Running);
        return activityId;
    }

    public void Trace(Guid activityId, string area, string title, string message)
    {
        Add(activityId, area, title, message, ActivityLogStatus.Info);
    }

    public void Complete(Guid activityId, string area, string title, string message)
    {
        Add(activityId, area, title, message, ActivityLogStatus.Success);
    }

    public void Warn(Guid activityId, string area, string title, string message)
    {
        Add(activityId, area, title, message, ActivityLogStatus.Warning);
    }

    public void Fail(Guid activityId, string area, string title, string message)
    {
        Add(activityId, area, title, message, ActivityLogStatus.Error);
    }

    public void Clear()
    {
        _entries.Clear();
        Changed?.Invoke();
    }

    public void UpsertJob(BackgroundJobClientSnapshot job)
    {
        var isNew = !_jobs.ContainsKey(job.JobId);
        _jobs[job.JobId] = job;

        var logKey = $"{job.Status}|{job.Stage}|{job.PercentComplete / 10}";
        var hasPreviousLog = _lastJobLogKeys.TryGetValue(job.JobId, out var previousLogKey);
        var previousParts = hasPreviousLog ? previousLogKey!.Split('|') : Array.Empty<string>();
        var statusChanged = previousParts.Length > 0 && !string.Equals(previousParts[0], job.Status, StringComparison.Ordinal);
        var stageChanged = previousParts.Length > 1 && !string.Equals(previousParts[1], job.Stage, StringComparison.Ordinal);
        var shouldLog = isNew || !hasPreviousLog || statusChanged || stageChanged;

        if (shouldLog)
        {
            _lastJobLogKeys[job.JobId] = logKey;
            Add(
                job.JobId,
                "Background Job",
                job.Title,
                $"{job.Stage}: {job.Detail}",
                ToActivityStatus(job.Status));
            return;
        }

        Changed?.Invoke();
    }

    private void Add(Guid activityId, string area, string title, string message, ActivityLogStatus status)
    {
        _entries.Insert(0, new ActivityLogEntry(
            Guid.NewGuid(),
            activityId,
            DateTimeOffset.Now,
            area,
            title,
            message,
            status));

        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        }

        Changed?.Invoke();
    }

    private static ActivityLogStatus ToActivityStatus(string status) => status switch
    {
        "Queued" or "Running" => ActivityLogStatus.Running,
        "Succeeded" => ActivityLogStatus.Success,
        "Failed" => ActivityLogStatus.Error,
        "Cancelled" => ActivityLogStatus.Warning,
        _ => ActivityLogStatus.Info
    };
}

public sealed record ActivityLogEntry(
    Guid Id,
    Guid ActivityId,
    DateTimeOffset Timestamp,
    string Area,
    string Title,
    string Message,
    ActivityLogStatus Status);

public enum ActivityLogStatus
{
    Info,
    Running,
    Success,
    Warning,
    Error
}
