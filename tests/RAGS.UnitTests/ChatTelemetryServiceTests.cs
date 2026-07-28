using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.Planning;

namespace RAGS.UnitTests;

public class ChatTelemetryServiceTests
{
    [Fact]
    public void BuildTelemetry_includes_all_capture_fields()
    {
        var service = new ChatTelemetryService();
        var plan = CreatePlan();
        var stats = new ChatCompletionStats
        {
            ElapsedSeconds = 1.5,
            EstimatedPromptTokens = 100,
            EstimatedCompletionTokens = 50,
            RetrievedContextCount = 10,
            CitationCount = 5,
            AlignmentConfidence = 0.85,
            ConfidenceBasis = "Provider-reported metrics."
        };

        var telemetry = service.BuildTelemetry(
            Guid.NewGuid(),
            plan,
            stats,
            TimeSpan.FromSeconds(2.0),
            llmCallCount: 1,
            usedProviderMetrics: true);

        Assert.Equal(2.0, telemetry.ElapsedSeconds, precision: 3);
        Assert.Equal(100, telemetry.PromptTokens);
        Assert.Equal(50, telemetry.CompletionTokens);
        Assert.True(telemetry.TokensPerSecond > 0);
        Assert.Equal(10, telemetry.RetrievalCount);
        Assert.Equal(5, telemetry.CitationCount);
        Assert.Equal(1, telemetry.LlmCallCount);
        Assert.True(telemetry.UsedProviderMetrics);
        Assert.Equal(0.85, telemetry.AlignmentConfidence, precision: 3);
        Assert.Equal("Provider-reported metrics.", telemetry.ConfidenceBasis);
        Assert.Equal(plan.EstimatedSecondsMin, telemetry.EstimatedSecondsMin);
        Assert.Equal(plan.EstimatedSecondsMax, telemetry.EstimatedSecondsMax);
        Assert.NotEmpty(telemetry.EstimateComparisonSummary);
    }

    [Fact]
    public void BuildTelemetry_falls_back_to_heuristics_when_stats_null()
    {
        var service = new ChatTelemetryService();
        var plan = CreatePlan();

        var telemetry = service.BuildTelemetry(
            Guid.NewGuid(),
            plan,
            null,
            TimeSpan.FromSeconds(1.0),
            llmCallCount: 0,
            usedProviderMetrics: false);

        Assert.Equal(0, telemetry.PromptTokens);
        Assert.Equal(0, telemetry.CompletionTokens);
        Assert.Equal(0, telemetry.RetrievalCount);
        Assert.False(telemetry.UsedProviderMetrics);
        Assert.Contains("No provider metrics were available", telemetry.ConfidenceBasis);
    }

    [Fact]
    public void CompareEstimate_produces_summary_for_within_range()
    {
        var service = new ChatTelemetryService();
        var plan = new ChatPlanRecord
        {
            PlanId = Guid.NewGuid(),
            EstimatedSecondsMin = 1,
            EstimatedSecondsMax = 10,
            EstimatedInputTokens = 50,
            EstimatedOutputTokens = 50,
            EstimatedLlmCalls = 1,
            EstimatedRetrievalCount = 5
        };
        var telemetry = new ChatExecutionTelemetry
        {
            ElapsedSeconds = 5.0,
            PromptTokens = 50,
            CompletionTokens = 50,
            RetrievalCount = 5,
            CitationCount = 2,
            LlmCallCount = 1,
            AlignmentConfidence = 0.9,
            ConfidenceBasis = "Provider metrics."
        };

        var comparison = service.CompareEstimate(plan, telemetry);

        Assert.Contains("within estimate", comparison.Summary);
        Assert.Contains("Duration:", comparison.Summary);
        Assert.Contains("Tokens:", comparison.Summary);
    }

    [Fact]
    public void CompareEstimate_handles_zero_estimates_gracefully()
    {
        var service = new ChatTelemetryService();
        var plan = new ChatPlanRecord { PlanId = Guid.NewGuid() };
        var telemetry = new ChatExecutionTelemetry
        {
            ElapsedSeconds = 1.2,
            PromptTokens = 10,
            CompletionTokens = 5,
            RetrievalCount = 3,
            LlmCallCount = 1,
            AlignmentConfidence = 0.5
        };

        var comparison = service.CompareEstimate(plan, telemetry);

        Assert.Contains("no estimate", comparison.Summary);
    }

    private static ChatPlanRecord CreatePlan()
    {
        return new ChatPlanRecord
        {
            PlanId = Guid.NewGuid(),
            EstimatedSecondsMin = 1,
            EstimatedSecondsMax = 5,
            EstimatedInputTokens = 100,
            EstimatedOutputTokens = 50,
            EstimatedLlmCalls = 1,
            EstimatedRetrievalCount = 10
        };
    }
}
