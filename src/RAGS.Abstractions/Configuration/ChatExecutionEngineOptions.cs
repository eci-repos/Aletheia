namespace Aletheia.RAGS.Abstractions.Configuration;

public sealed class ChatExecutionEngineOptions
{
    public const string SectionName = "ChatExecutionEngine";

    public int DefaultStepTimeoutSeconds { get; set; } = 30;

    public int OverallJobTimeoutSeconds { get; set; } = 300;

    public int HeartbeatIntervalSeconds { get; set; } = 30;

    public int LongWaitHeartbeatIntervalSeconds { get; set; } = 120;

    public int HeartbeatWatchdogMissedThreshold { get; set; } = 3;

    public int SmallCorpusDocumentThreshold { get; set; } = 5;

    public int SmallCorpusTimeoutSeconds { get; set; } = 5;

    public bool UsePlanStepsForProgress { get; set; } = false;
}
