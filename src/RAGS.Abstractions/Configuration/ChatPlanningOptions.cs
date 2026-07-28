namespace Aletheia.RAGS.Abstractions.Configuration;

public sealed class ChatPlanningOptions
{
    public const string SectionName = "ChatPlanning";

    public int ApprovalThresholdSeconds { get; set; } = 60;

    public int ApprovalThresholdLlmCalls { get; set; } = 3;

    public int ApprovalThresholdRetrievalCount { get; set; } = 20;

    public int FastPathMaxSeconds { get; set; } = 10;

    public int FastPathMaxLlmCalls { get; set; } = 1;

    public int FastPathMaxRetrievalCount { get; set; } = 5;

    public int PlanExpirationMinutes { get; set; } = 15;

    public int DefaultTopK { get; set; } = 8;

    public int CorpusTopK { get; set; } = 50;

    public int TimelineMinYears { get; set; } = 2;
}
