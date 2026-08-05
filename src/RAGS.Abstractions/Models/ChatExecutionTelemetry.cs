namespace Aletheia.RAGS.Abstractions.Models;

public sealed class ChatExecutionTelemetry
{
    public Guid JobId { get; init; }

    public Guid PlanId { get; init; }

    public double ElapsedSeconds { get; init; }

    public int PromptTokens { get; init; }

    public int CompletionTokens { get; init; }

    public double TokensPerSecond { get; init; }

    public int RetrievalCount { get; init; }

    public int CitationCount { get; init; }

    public int LlmCallCount { get; init; }

    public int EstimatedSecondsMin { get; init; }

    public int EstimatedSecondsMax { get; init; }

    public int EstimatedInputTokens { get; init; }

    public int EstimatedOutputTokens { get; init; }

    public int EstimatedLlmCalls { get; init; }

    public int EstimatedRetrievalCount { get; init; }

    public double AlignmentConfidence { get; init; }

    public string ConfidenceBasis { get; init; } = string.Empty;

    public string EstimateComparisonSummary { get; init; } = string.Empty;

    public bool UsedProviderMetrics { get; init; }

    public string ToolName { get; init; } = string.Empty;

    public int ToolInvocationCount { get; init; }

    public string RetrievalStrategy { get; init; } = string.Empty;
}

public sealed class ChatEstimateComparison
{
    public double ElapsedSeconds { get; init; }

    public int ActualPromptTokens { get; init; }

    public int ActualCompletionTokens { get; init; }

    public int ActualRetrievalCount { get; init; }

    public int ActualCitationCount { get; init; }

    public int ActualLlmCallCount { get; init; }

    public int EstimatedSecondsMin { get; init; }

    public int EstimatedSecondsMax { get; init; }

    public int EstimatedInputTokens { get; init; }

    public int EstimatedOutputTokens { get; init; }

    public int EstimatedLlmCalls { get; init; }

    public int EstimatedRetrievalCount { get; init; }

    public double AlignmentConfidence { get; init; }

    public string ConfidenceBasis { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;
}
