using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.Extensions.Options;

namespace Aletheia.RAGS.Application.Planning;

public sealed class ChatPlanningService : IChatPlanningService
{
    private readonly ChatPlanningOptions _options;

    public ChatPlanningService(IOptions<ChatPlanningOptions>? options = null)
    {
        _options = options?.Value ?? new ChatPlanningOptions();
    }

    public Task<Result<PromptAnalysis>> AnalyzePromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(prompt));
        }

        var normalized = NormalizePrompt(prompt);
        var signals = DetectSignals(normalized);
        var isBroad = IsBroadCorpusRequest(signals);
        var mode = InferMode(signals, isBroad);
        var tokenEstimate = EstimateTokens(prompt);
        var expensive = IsExpensive(mode, signals);
        var requiresApproval = expensive || mode == ChatExecutionMode.CorpusAnalysis;

        var analysis = new PromptAnalysis
        {
            NormalizedPrompt = normalized,
            SuggestedMode = mode,
            DetectedIntentSignals = signals,
            IsBroadCorpusRequest = isBroad,
            IsExpensive = expensive,
            RequiresApproval = requiresApproval,
            EstimatedPromptTokens = tokenEstimate,
            Confidence = ComputeConfidence(signals)
        };

        return Task.FromResult(Result<PromptAnalysis>.Success(analysis));
    }

    public async Task<Result<ChatExecutionPlan>> CreatePlanAsync(string prompt, PromptAnalysis? analysis = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(prompt));
        }

        var resolvedAnalysis = analysis;
        if (resolvedAnalysis is null)
        {
            var analysisResult = await AnalyzePromptAsync(prompt, cancellationToken).ConfigureAwait(false);
            if (analysisResult.IsFailure)
            {
                return Result<ChatExecutionPlan>.Failure(analysisResult.Error ?? "Analysis failed.");
            }

            resolvedAnalysis = analysisResult.Value;
        }

        if (resolvedAnalysis is null)
        {
            return Result<ChatExecutionPlan>.Failure("Analysis could not be produced.");
        }

        var plan = BuildPlan(prompt, resolvedAnalysis);
        return await EstimatePlanAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    public Task<Result<ChatExecutionPlan>> EstimatePlanAsync(ChatExecutionPlan plan, CancellationToken cancellationToken = default)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        var requiresApproval = RequiresApproval(plan);
        var estimated = new ChatExecutionPlan
        {
            PlanId = plan.PlanId,
            Prompt = plan.Prompt,
            Mode = plan.Mode,
            Steps = plan.Steps,
            EstimatedSecondsMin = plan.EstimatedSecondsMin,
            EstimatedSecondsMax = plan.EstimatedSecondsMax,
            EstimatedLlmCalls = plan.EstimatedLlmCalls,
            EstimatedInputTokens = plan.EstimatedInputTokens,
            EstimatedOutputTokens = plan.EstimatedOutputTokens,
            EstimatedRetrievalCount = plan.EstimatedRetrievalCount,
            RequiresApproval = requiresApproval,
            RequiresToolCall = plan.RequiresToolCall,
            ToolName = plan.ToolName,
            ToolArguments = plan.ToolArguments,
            CreatedAt = plan.CreatedAt,
            ExpiresAt = plan.ExpiresAt
        };
        return Task.FromResult(Result<ChatExecutionPlan>.Success(estimated));
    }

    public bool RequiresApproval(ChatExecutionPlan plan)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        return plan.Mode == ChatExecutionMode.CorpusAnalysis
            || plan.EstimatedSecondsMax >= _options.ApprovalThresholdSeconds
            || plan.EstimatedLlmCalls >= _options.ApprovalThresholdLlmCalls
            || plan.EstimatedRetrievalCount >= _options.ApprovalThresholdRetrievalCount;
    }

    private ChatExecutionPlan BuildPlan(string prompt, PromptAnalysis analysis)
    {
        var requiresToolCall = RequiresToolCall(analysis);
        var toolName = requiresToolCall ? SelectToolName(analysis) : string.Empty;
        var toolArguments = requiresToolCall ? BuildToolArguments(prompt, analysis) : new Dictionary<string, string>();
        var steps = BuildSteps(analysis, requiresToolCall, toolName);
        var (minSeconds, maxSeconds) = EstimateDuration(analysis);
        var llmCalls = EstimateLlmCalls(analysis);
        var inputTokens = analysis.EstimatedPromptTokens;
        var outputTokens = EstimateOutputTokens(analysis);
        var retrievalCount = EstimateRetrievalCount(analysis);

        return new ChatExecutionPlan
        {
            PlanId = Guid.NewGuid(),
            Prompt = prompt,
            Mode = analysis.SuggestedMode,
            Steps = steps,
            EstimatedSecondsMin = minSeconds,
            EstimatedSecondsMax = maxSeconds,
            EstimatedLlmCalls = llmCalls,
            EstimatedInputTokens = inputTokens,
            EstimatedOutputTokens = outputTokens,
            EstimatedRetrievalCount = retrievalCount,
            RequiresApproval = false,
            RequiresToolCall = requiresToolCall,
            ToolName = toolName,
            ToolArguments = toolArguments,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.PlanExpirationMinutes)
        };
    }

    private static IReadOnlyList<string> BuildSteps(PromptAnalysis analysis, bool requiresToolCall, string toolName)
    {
        var steps = new List<string> { "Classify user intent" };

        if (requiresToolCall)
        {
            steps.Add($"Call repository tool: {toolName}");
            steps.Add("Verify tool returned internal context before synthesis");
        }

        switch (analysis.SuggestedMode)
        {
            case ChatExecutionMode.FastPath:
                if (!requiresToolCall)
                {
                    steps.Add("Answer directly without retrieval");
                }
                else
                {
                    steps.Add("Synthesize answer with citations");
                }
                break;
            case ChatExecutionMode.Retrieval:
                if (!requiresToolCall)
                {
                    steps.Add("Retrieve top-k relevant chunks");
                }
                steps.Add("Synthesize answer with citations");
                break;
            case ChatExecutionMode.CorpusAnalysis:
                if (!requiresToolCall)
                {
                    steps.Add("Retrieve broad corpus context");
                    steps.Add("Aggregate findings across sources");
                }
                steps.Add("Synthesize answer with citations");
                break;
            case ChatExecutionMode.ComparativeAnalysis:
                if (!requiresToolCall)
                {
                    steps.Add("Retrieve candidate chunks for each subject");
                    steps.Add("Compare aligned attributes");
                }
                steps.Add("Present comparison with citations");
                break;
            case ChatExecutionMode.TimelineAnalysis:
                if (!requiresToolCall)
                {
                    steps.Add("Extract temporal references");
                    steps.Add("Retrieve dated evidence across years");
                }
                steps.Add("Order events and synthesize timeline");
                break;
            case ChatExecutionMode.StructuredSynthesis:
                if (!requiresToolCall)
                {
                    steps.Add("Retrieve relevant chunks");
                    steps.Add("Structure output by requested format");
                }
                steps.Add("Fill each section with cited evidence");
                break;
            default:
                if (!requiresToolCall)
                {
                    steps.Add("Retrieve relevant context");
                }
                steps.Add("Synthesize response");
                break;
        }

        steps.Add("Return final response");
        return steps;
    }



    private static bool RequiresToolCall(PromptAnalysis analysis)
    {
        return analysis.DetectedIntentSignals.Contains("rfp", StringComparer.OrdinalIgnoreCase)
            || analysis.DetectedIntentSignals.Contains("wrags", StringComparer.OrdinalIgnoreCase)
            || analysis.DetectedIntentSignals.Contains("lazy", StringComparer.OrdinalIgnoreCase)
            || analysis.DetectedIntentSignals.Contains("lazygraphrag", StringComparer.OrdinalIgnoreCase)
            || IsDocumentRequirementPrompt(analysis.DetectedIntentSignals)
            || analysis.DetectedIntentSignals.Contains("corpus-wide", StringComparer.OrdinalIgnoreCase)
            || analysis.DetectedIntentSignals.Contains("exhaustive", StringComparer.OrdinalIgnoreCase)
            || analysis.DetectedIntentSignals.Contains("temporal", StringComparer.OrdinalIgnoreCase)
            || (analysis.DetectedIntentSignals.Contains("summarization", StringComparer.OrdinalIgnoreCase)
                && analysis.IsBroadCorpusRequest);
    }

    private static string SelectToolName(PromptAnalysis analysis)
    {
        if (analysis.DetectedIntentSignals.Contains("lazy", StringComparer.OrdinalIgnoreCase)
            || analysis.DetectedIntentSignals.Contains("lazygraphrag", StringComparer.OrdinalIgnoreCase))
        {
            return "AletheiaKnowledgePlugin.SearchLazyGraphRag";
        }

        if (analysis.IsBroadCorpusRequest
            && !analysis.DetectedIntentSignals.Contains("rfp", StringComparer.OrdinalIgnoreCase)
            && !IsDocumentRequirementPrompt(analysis.DetectedIntentSignals))
        {
            return "AletheiaKnowledgePlugin.SearchGraphRag";
        }

        return "AletheiaKnowledgePlugin.SearchRags";
    }

    private static bool IsDocumentRequirementPrompt(IReadOnlyList<string> signals)
    {
        return signals.Contains("requirements", StringComparer.OrdinalIgnoreCase)
            && signals.Contains("document-scoped", StringComparer.OrdinalIgnoreCase);
    }

        private static IReadOnlyDictionary<string, string> BuildToolArguments(string prompt, PromptAnalysis analysis)
        {
            var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["query"] = prompt
            };

            var retrievalCount = analysis.SuggestedMode switch
            {
                ChatExecutionMode.FastPath => 0,
                ChatExecutionMode.Retrieval => 8,
                ChatExecutionMode.CorpusAnalysis => 50,
                ChatExecutionMode.ComparativeAnalysis => 16,
                ChatExecutionMode.TimelineAnalysis => 50,
                ChatExecutionMode.StructuredSynthesis => 8,
                _ => 8
            };

            arguments["topK"] = retrievalCount > 0 ? retrievalCount.ToString() : "8";

            // Sprint 46: if the prompt mentions a specific document (e.g., "CMP 2026"), add a source‑id filter.
            // For now we pass a placeholder identifier that downstream tooling can resolve to an actual source GUID.
            var match = System.Text.RegularExpressions.Regex.Match(prompt, @"CMP\s*([0-9]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                // Placeholder format – the SearchRags implementation can translate this to the real SourceId.
                var docName = $"CMP-{match.Groups[1].Value}";
                arguments["sourceId"] = docName;
            }

            return arguments;
        }

    private (int Min, int Max) EstimateDuration(PromptAnalysis analysis)
    {
        return analysis.SuggestedMode switch
        {
            ChatExecutionMode.FastPath => (1, _options.FastPathMaxSeconds),
            ChatExecutionMode.Retrieval => (3, 20),
            ChatExecutionMode.CorpusAnalysis => (30, 180),
            ChatExecutionMode.ComparativeAnalysis => (15, 90),
            ChatExecutionMode.TimelineAnalysis => (20, 120),
            ChatExecutionMode.StructuredSynthesis => (10, 60),
            _ => (5, 30)
        };
    }

    private int EstimateLlmCalls(PromptAnalysis analysis)
    {
        return analysis.SuggestedMode switch
        {
            ChatExecutionMode.FastPath => _options.FastPathMaxLlmCalls,
            ChatExecutionMode.Retrieval => 1,
            ChatExecutionMode.CorpusAnalysis => Math.Max(2, _options.ApprovalThresholdLlmCalls),
            ChatExecutionMode.ComparativeAnalysis => 2,
            ChatExecutionMode.TimelineAnalysis => 2,
            ChatExecutionMode.StructuredSynthesis => 1,
            _ => 1
        };
    }

    private int EstimateOutputTokens(PromptAnalysis analysis)
    {
        return analysis.SuggestedMode switch
        {
            ChatExecutionMode.FastPath => Math.Max(50, analysis.EstimatedPromptTokens / 2),
            ChatExecutionMode.Retrieval => Math.Max(150, analysis.EstimatedPromptTokens),
            ChatExecutionMode.CorpusAnalysis => Math.Max(500, analysis.EstimatedPromptTokens * 3),
            ChatExecutionMode.ComparativeAnalysis => Math.Max(300, analysis.EstimatedPromptTokens * 2),
            ChatExecutionMode.TimelineAnalysis => Math.Max(300, analysis.EstimatedPromptTokens * 2),
            ChatExecutionMode.StructuredSynthesis => Math.Max(250, analysis.EstimatedPromptTokens * 2),
            _ => Math.Max(150, analysis.EstimatedPromptTokens)
        };
    }

    private int EstimateRetrievalCount(PromptAnalysis analysis)
    {
        return analysis.SuggestedMode switch
        {
            ChatExecutionMode.FastPath => 0,
            ChatExecutionMode.Retrieval => _options.DefaultTopK,
            ChatExecutionMode.CorpusAnalysis => _options.CorpusTopK,
            ChatExecutionMode.ComparativeAnalysis => _options.DefaultTopK * 2,
            ChatExecutionMode.TimelineAnalysis => _options.CorpusTopK,
            ChatExecutionMode.StructuredSynthesis => _options.DefaultTopK,
            _ => _options.DefaultTopK
        };
    }

    private static IReadOnlyList<string> DetectSignals(string normalizedPrompt)
    {
        var signals = new List<string>();
        var lower = normalizedPrompt;

        if (ContainsAny(lower, "last", "past", "previous", "since", "recent", "years", "year"))
        {
            signals.Add("temporal");
        }

        if (ContainsAny(lower, "all documents", "all rfps", "corpus", "entire corpus", "across the corpus", "summarize corpus", "throughout the repository", "repository"))
        {
            signals.Add("corpus-wide");
        }

        if (ContainsAny(lower, "rpf", "rfp", "rfps", "request for proposal", "procurement", "opportunities", "opportunity"))
        {
            signals.Add("rfp");
        }

        if (ContainsAny(lower, "cmp", "document", "docx", "file", "artifact", "engagement"))
        {
            signals.Add("document-scoped");
        }

        if (ContainsAny(lower, "requirement", "requirements", "required", "feature", "features", "capability", "capabilities"))
        {
            signals.Add("requirements");
        }

        if (ContainsAny(lower, "wrags", "wiki", "knowledge base", "kb"))
        {
            signals.Add("wrags");
        }

        if (ContainsAny(lower, "lazygraphrag", "lazy graph", "lazy graph rag", "lazy enrichment"))
        {
            signals.Add("lazygraphrag");
        }

        if (ContainsAny(lower, "every", "each", "all instances", "identify every", "list all", "all found", "found features", "required features"))
        {
            signals.Add("exhaustive");
        }

        if (ContainsAny(lower, "compare", "comparison", "versus", "vs", "differences between", "similarities between"))
        {
            signals.Add("comparative");
        }

        if (ContainsAny(lower, "timeline", "chronology", "over time", "evolution"))
        {
            signals.Add("timeline");
        }

        if (ContainsAny(lower, "matrix", "table", "spreadsheet", "rows and columns"))
        {
            signals.Add("structured");
        }

        if (ContainsAny(lower, "summarize", "summary", "overview"))
        {
            signals.Add("summarization");
        }

        if (ContainsAny(lower, "who", "what", "when", "where", "why", "how"))
        {
            signals.Add("factual");
        }

        return signals;
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        foreach (var term in terms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBroadCorpusRequest(IReadOnlyList<string> signals)
    {
        return signals.Contains("corpus-wide", StringComparer.OrdinalIgnoreCase)
            || signals.Contains("exhaustive", StringComparer.OrdinalIgnoreCase)
            || signals.Contains("rfp", StringComparer.OrdinalIgnoreCase)
            || signals.Contains("wrags", StringComparer.OrdinalIgnoreCase);
    }

    private static ChatExecutionMode InferMode(IReadOnlyList<string> signals, bool isBroad)
    {
        if (isBroad)
        {
            if (signals.Contains("temporal", StringComparer.OrdinalIgnoreCase))
            {
                return ChatExecutionMode.TimelineAnalysis;
            }

            return ChatExecutionMode.CorpusAnalysis;
        }

        if (signals.Contains("timeline", StringComparer.OrdinalIgnoreCase)
            || signals.Contains("temporal", StringComparer.OrdinalIgnoreCase))
        {
            return ChatExecutionMode.TimelineAnalysis;
        }

        if (signals.Contains("comparative", StringComparer.OrdinalIgnoreCase))
        {
            return ChatExecutionMode.ComparativeAnalysis;
        }

        if (signals.Contains("structured", StringComparer.OrdinalIgnoreCase))
        {
            return ChatExecutionMode.StructuredSynthesis;
        }

        if (signals.Contains("corpus-wide", StringComparer.OrdinalIgnoreCase)
            || signals.Contains("exhaustive", StringComparer.OrdinalIgnoreCase)
            || signals.Contains("summarization", StringComparer.OrdinalIgnoreCase)
            || signals.Contains("rfp", StringComparer.OrdinalIgnoreCase)
            || signals.Contains("wrags", StringComparer.OrdinalIgnoreCase))
        {
            return ChatExecutionMode.CorpusAnalysis;
        }

        if (signals.Contains("factual", StringComparer.OrdinalIgnoreCase)
            || signals.Contains("retrieval", StringComparer.OrdinalIgnoreCase))
        {
            return ChatExecutionMode.Retrieval;
        }

        return ChatExecutionMode.FastPath;
    }

    private static bool IsExpensive(ChatExecutionMode mode, IReadOnlyList<string> signals)
    {
        return mode switch
        {
            ChatExecutionMode.CorpusAnalysis => true,
            ChatExecutionMode.ComparativeAnalysis => true,
            ChatExecutionMode.TimelineAnalysis => true,
            ChatExecutionMode.StructuredSynthesis => signals.Contains("corpus-wide", StringComparer.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static int EstimateTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var wordEstimate = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length * 1.33d;
        var characterEstimate = value.Length / 4d;
        return Math.Max(1, (int)Math.Round(Math.Max(wordEstimate, characterEstimate)));
    }

    private static int ComputeConfidence(IReadOnlyList<string> signals)
    {
        return signals.Count == 0 ? 30 : Math.Min(100, 50 + signals.Count * 10);
    }

    private static string NormalizePrompt(string prompt)
    {
        return prompt.Trim().ReplaceLineEndings(" ");
    }
}
