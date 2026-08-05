namespace Aletheia.RAGS.Abstractions.Configuration;

public sealed class ChatExecutionEngineOptions
{
    public const string SectionName = "ChatExecutionEngine";

    public int DefaultStepTimeoutSeconds { get; set; } = 30;

    public int MandatoryToolTimeoutSeconds { get; set; } = 180;

    public int OverallJobTimeoutSeconds { get; set; } = 600;

    public int HeartbeatIntervalSeconds { get; set; } = 10;

    public int LongWaitHeartbeatIntervalSeconds { get; set; } = 30;

    public int HeartbeatWatchdogMissedThreshold { get; set; } = 6;

    public int MaxConcurrentChatJobs { get; set; } = 3;

    public int SmallCorpusDocumentThreshold { get; set; } = 5;

    public int SmallCorpusTimeoutSeconds { get; set; } = 5;

    public int HydrationTimeoutSeconds { get; set; } = 30;

    public bool UsePlanStepsForProgress { get; set; } = false;
}
