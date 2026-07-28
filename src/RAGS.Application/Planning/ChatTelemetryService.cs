using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Application.Planning;

public interface IChatTelemetryService
{
    ChatExecutionTelemetry BuildTelemetry(
        Guid jobId,
        ChatPlanRecord plan,
        ChatCompletionStats? stats,
        TimeSpan elapsed,
        int llmCallCount,
        bool usedProviderMetrics,
        string? toolName = null,
        int toolInvocationCount = 0);

    ChatEstimateComparison CompareEstimate(ChatPlanRecord plan, ChatExecutionTelemetry telemetry);
}

public sealed class ChatTelemetryService : IChatTelemetryService
{
    public ChatExecutionTelemetry BuildTelemetry(
        Guid jobId,
        ChatPlanRecord plan,
        ChatCompletionStats? stats,
        TimeSpan elapsed,
        int llmCallCount,
        bool usedProviderMetrics,
        string? toolName = null,
        int toolInvocationCount = 0)
    {
        var elapsedSeconds = Math.Max(elapsed.TotalSeconds, 0.001d);
        var promptTokens = stats?.EstimatedPromptTokens ?? 0;
        var completionTokens = stats?.EstimatedCompletionTokens ?? 0;
        var tokensPerSecond = completionTokens / elapsedSeconds;
        var retrievalCount = stats?.RetrievedContextCount ?? 0;
        var citationCount = stats?.CitationCount ?? 0;
        var confidence = stats?.AlignmentConfidence ?? 0d;
        var basis = stats?.ConfidenceBasis ?? "No provider metrics were available; estimates are heuristic.";

        var telemetry = new ChatExecutionTelemetry
        {
            JobId = jobId,
            PlanId = plan.PlanId,
            ElapsedSeconds = Math.Round(elapsedSeconds, 3),
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TokensPerSecond = Math.Round(tokensPerSecond, 2),
            RetrievalCount = retrievalCount,
            CitationCount = citationCount,
            LlmCallCount = Math.Max(llmCallCount, 1),
            EstimatedSecondsMin = plan.EstimatedSecondsMin,
            EstimatedSecondsMax = plan.EstimatedSecondsMax,
            EstimatedInputTokens = plan.EstimatedInputTokens,
            EstimatedOutputTokens = plan.EstimatedOutputTokens,
            EstimatedLlmCalls = plan.EstimatedLlmCalls,
            EstimatedRetrievalCount = plan.EstimatedRetrievalCount,
            AlignmentConfidence = Math.Round(confidence, 3),
            ConfidenceBasis = basis,
            UsedProviderMetrics = usedProviderMetrics,
            ToolName = toolName ?? string.Empty,
            ToolInvocationCount = toolInvocationCount,
            EstimateComparisonSummary = BuildComparisonSummary(
                plan,
                new ChatExecutionTelemetry
                {
                    ElapsedSeconds = Math.Round(elapsedSeconds, 3),
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    RetrievalCount = retrievalCount,
                    CitationCount = citationCount,
                    LlmCallCount = Math.Max(llmCallCount, 1),
                    EstimatedSecondsMin = plan.EstimatedSecondsMin,
                    EstimatedSecondsMax = plan.EstimatedSecondsMax,
                    EstimatedInputTokens = plan.EstimatedInputTokens,
                    EstimatedOutputTokens = plan.EstimatedOutputTokens,
                    EstimatedLlmCalls = plan.EstimatedLlmCalls,
                    EstimatedRetrievalCount = plan.EstimatedRetrievalCount
                })
        };

        return telemetry;
    }

    public ChatEstimateComparison CompareEstimate(ChatPlanRecord plan, ChatExecutionTelemetry telemetry)
    {
        var summary = BuildComparisonSummary(plan, telemetry);
        return new ChatEstimateComparison
        {
            ElapsedSeconds = telemetry.ElapsedSeconds,
            ActualPromptTokens = telemetry.PromptTokens,
            ActualCompletionTokens = telemetry.CompletionTokens,
            ActualRetrievalCount = telemetry.RetrievalCount,
            ActualCitationCount = telemetry.CitationCount,
            ActualLlmCallCount = telemetry.LlmCallCount,
            EstimatedSecondsMin = plan.EstimatedSecondsMin,
            EstimatedSecondsMax = plan.EstimatedSecondsMax,
            EstimatedInputTokens = plan.EstimatedInputTokens,
            EstimatedOutputTokens = plan.EstimatedOutputTokens,
            EstimatedLlmCalls = plan.EstimatedLlmCalls,
            EstimatedRetrievalCount = plan.EstimatedRetrievalCount,
            AlignmentConfidence = telemetry.AlignmentConfidence,
            ConfidenceBasis = telemetry.ConfidenceBasis,
            Summary = summary
        };
    }

    private static string BuildComparisonSummary(ChatPlanRecord plan, ChatExecutionTelemetry telemetry)
    {
        var durationComparison = CompareDuration(plan, telemetry);
        var tokenComparison = CompareTokens(plan, telemetry);
        var retrievalComparison = CompareRetrieval(plan, telemetry);
        var callComparison = CompareLlmCalls(plan, telemetry);

        return $"Duration: {durationComparison}; Tokens: {tokenComparison}; Retrieval: {retrievalComparison}; Model calls: {callComparison}.";
    }

    private static string CompareDuration(ChatPlanRecord plan, ChatExecutionTelemetry telemetry)
    {
        if (plan.EstimatedSecondsMax <= 0)
        {
            return $"actual {telemetry.ElapsedSeconds:F1}s (no estimate)";
        }

        var within = telemetry.ElapsedSeconds >= plan.EstimatedSecondsMin && telemetry.ElapsedSeconds <= plan.EstimatedSecondsMax;
        var delta = telemetry.ElapsedSeconds - ((plan.EstimatedSecondsMin + plan.EstimatedSecondsMax) / 2.0);
        var sign = delta >= 0 ? "+" : string.Empty;
        return within
            ? $"actual {telemetry.ElapsedSeconds:F1}s within estimate {plan.EstimatedSecondsMin}-{plan.EstimatedSecondsMax}s"
            : $"actual {telemetry.ElapsedSeconds:F1}s outside estimate {plan.EstimatedSecondsMin}-{plan.EstimatedSecondsMax}s ({sign}{delta:F1}s)";
    }

    private static string CompareTokens(ChatPlanRecord plan, ChatExecutionTelemetry telemetry)
    {
        var estimated = plan.EstimatedInputTokens + plan.EstimatedOutputTokens;
        var actual = telemetry.PromptTokens + telemetry.CompletionTokens;
        if (estimated <= 0)
        {
            return $"actual {actual} (no estimate)";
        }

        var ratio = actual / (double)estimated;
        return $"actual {actual} vs estimated {estimated} ({ratio:P0})";
    }

    private static string CompareRetrieval(ChatPlanRecord plan, ChatExecutionTelemetry telemetry)
    {
        if (plan.EstimatedRetrievalCount <= 0)
        {
            return $"actual {telemetry.RetrievalCount} (no estimate)";
        }

        var ratio = telemetry.RetrievalCount / (double)plan.EstimatedRetrievalCount;
        return $"actual {telemetry.RetrievalCount} vs estimated {plan.EstimatedRetrievalCount} ({ratio:P0})";
    }

    private static string CompareLlmCalls(ChatPlanRecord plan, ChatExecutionTelemetry telemetry)
    {
        if (plan.EstimatedLlmCalls <= 0)
        {
            return $"actual {telemetry.LlmCallCount} (no estimate)";
        }

        var ratio = telemetry.LlmCallCount / (double)plan.EstimatedLlmCalls;
        return $"actual {telemetry.LlmCallCount} vs estimated {plan.EstimatedLlmCalls} ({ratio:P0})";
    }
}
