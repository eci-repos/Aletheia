using System.Collections.Concurrent;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Threading.Channels;

namespace Aletheia.RAGS.Application.Planning;

public interface IChatExecutionEngine : IHostedService, IChatExecutionService
{
}

public sealed class ChatExecutionEngine : BackgroundService, IChatExecutionEngine
{
    private const int MaxJobs = 200;

    private readonly ConcurrentDictionary<Guid, ChatJobState> _jobs = new();
    private readonly ConcurrentQueue<Guid> _jobOrder = new();
    private readonly Channel<ChatJobWorkItem> _queue = Channel.CreateUnbounded<ChatJobWorkItem>();

    private static readonly List<string> ProgressStages = new()
    {
        "Planning",
        "Finding candidate sources",
        "Filtering sources",
        "Retrieving context",
        "Call repository tool",
        "Expanding graph context",
        "Extracting requested facts",
        "Validating citations",
        "Synthesizing answer",
        "Finalizing telemetry",
        "Completed"
    };

    private readonly IChatPlanApprovalService _planApprovalService;
    private readonly ICopilotService _copilotService;
    private readonly IRagsService _ragsService;
    private readonly IGraphRagService _graphRagService;
    private readonly ILazyGraphRagService _lazyGraphRagService;
    private readonly IGlobalGraphSearchService _globalGraphSearchService;
    private readonly IChatProgressStore _progressStore;
    private readonly IChatTelemetryService _telemetryService;
    private readonly ChatExecutionEngineOptions _options;
    private readonly ILogger<ChatExecutionEngine> _logger;

    public ChatExecutionEngine(
        IChatPlanApprovalService planApprovalService,
        ICopilotService copilotService,
        IRagsService ragsService,
        IGraphRagService graphRagService,
        ILazyGraphRagService lazyGraphRagService,
        IGlobalGraphSearchService globalGraphSearchService,
        IChatProgressStore progressStore,
        IChatTelemetryService telemetryService,
        IOptions<ChatExecutionEngineOptions> options,
        ILogger<ChatExecutionEngine> logger)
    {
        _planApprovalService = planApprovalService ?? throw new ArgumentNullException(nameof(planApprovalService));
        _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));
        _ragsService = ragsService ?? throw new ArgumentNullException(nameof(ragsService));
        _graphRagService = graphRagService ?? throw new ArgumentNullException(nameof(graphRagService));
        _lazyGraphRagService = lazyGraphRagService ?? throw new ArgumentNullException(nameof(lazyGraphRagService));
        _globalGraphSearchService = globalGraphSearchService ?? throw new ArgumentNullException(nameof(globalGraphSearchService));
        _progressStore = progressStore ?? throw new ArgumentNullException(nameof(progressStore));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<Result<ChatJobSnapshot>> StartAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(planId, cancellationToken);
    }

    public async Task<Result> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobId, out var state))
        {
            return Result.Failure("Job not found.");
        }

        state.Cancel("Cancelled by user.");
        await _progressStore.FinalizeAsync(jobId, ChatJobStatus.Cancelled, null, "Cancelled by user.", cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public Task<Result<ChatJobSnapshot?>> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var snapshot = _jobs.TryGetValue(jobId, out var state)
            ? state.ToSnapshot()
            : null;

        return Task.FromResult(Result<ChatJobSnapshot?>.Success(snapshot));
    }

    public async Task<Result<ChatProgressRecord?>> GetProgressAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _progressStore.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<ChatJobSnapshot> List(int take = 50)
    {
        var resolvedTake = Math.Clamp(take, 1, MaxJobs);
        return _jobs.Values
            .Select(state => state.ToSnapshot())
            .OrderByDescending(job => job.CreatedAt)
            .Take(resolvedTake)
            .ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var watchdogTask = RunHeartbeatWatchdogAsync(watchdogCts.Token);

        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                if (!_jobs.TryGetValue(item.JobId, out var state))
                {
                    continue;
                }

                try
                {
                    await RunJobAsync(item, state, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    await FinalizeFailedAsync(item.JobId, state, "API host is shutting down.").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Chat job {JobId} failed.", item.JobId);
                    await FinalizeFailedAsync(item.JobId, state, ex.Message).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await watchdogCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await watchdogTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RunHeartbeatWatchdogAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var threshold = TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds * _options.HeartbeatWatchdogMissedThreshold);
            foreach (var kvp in _jobs.ToArray())
            {
                var state = kvp.Value;
                if (!state.IsActive || state.IsTerminal)
                {
                    continue;
                }

                if (now - state.LastHeartbeatAt > interval)
                {
                    var missed = state.IncrementMissedHeartbeat();
                    if (missed >= _options.HeartbeatWatchdogMissedThreshold)
                    {
                        var error = $"Engine heartbeat watchdog detected a stalled job (no heartbeat for more than {threshold.TotalSeconds:F0}s).";
                        _logger.LogWarning(error);
                        await FinalizeFailedAsync(kvp.Key, state, error).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private async Task<Result<ChatJobSnapshot>> EnqueueAsync(Guid planId, CancellationToken cancellationToken)
    {
        var planResult = await _planApprovalService.GetAsync(planId, cancellationToken).ConfigureAwait(false);
        if (planResult.IsFailure)
        {
            return Result<ChatJobSnapshot>.Failure(planResult.Error ?? "Failed to load plan.");
        }

        var plan = planResult.Value;
        if (plan is null)
        {
            return Result<ChatJobSnapshot>.Failure("Plan not found.");
        }

        if (plan.Status != ChatPlanStatus.Approved)
        {
            return Result<ChatJobSnapshot>.Failure($"Plan must be approved before execution. Current status: {plan.Status}.");
        }

        if (plan.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return Result<ChatJobSnapshot>.Failure($"Plan expired at {plan.ExpiresAt:O}. Re-approve the plan to execute.");
        }

        var jobId = Guid.NewGuid();
        var state = new ChatJobState(jobId, planId, plan.Prompt);
        _jobs[jobId] = state;
        _jobOrder.Enqueue(jobId);
        TrimOldJobs();

        var progress = CreateProgressRecord(jobId, plan);
        var saveProgressResult = await _progressStore.SaveAsync(progress, cancellationToken).ConfigureAwait(false);
        if (saveProgressResult.IsFailure)
        {
            state.Fail(saveProgressResult.Error ?? "Unable to persist progress.");
            return Result<ChatJobSnapshot>.Failure(saveProgressResult.Error ?? "Unable to persist progress.");
        }

        var item = new ChatJobWorkItem(jobId, plan);
        if (!_queue.Writer.TryWrite(item))
        {
            state.Fail("Unable to queue chat execution job.");
            return Result<ChatJobSnapshot>.Failure("Unable to queue chat execution job.");
        }

        return Result<ChatJobSnapshot>.Success(state.ToSnapshot());
    }

    private async Task RunJobAsync(ChatJobWorkItem item, ChatJobState state, CancellationToken cancellationToken)
    {
        using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overallCts.CancelAfter(TimeSpan.FromSeconds(_options.OverallJobTimeoutSeconds));
        var jobToken = overallCts.Token;

        var overallStopwatch = Stopwatch.StartNew();
        var llmCallCount = 0;

        await BeginStepAsync(item.JobId, "Planning", "Planning execution steps.", jobToken).ConfigureAwait(false);
        await _progressStore.FinalizeAsync(item.JobId, ChatJobStatus.Running, null, null, jobToken).ConfigureAwait(false);
        state.Start("Planning", "Planning execution steps.");
        await CompleteStepAsync(item.JobId, "Planning", jobToken).ConfigureAwait(false);

        await BeginStepAsync(item.JobId, "Finding candidate sources", "Finding candidate sources.", jobToken).ConfigureAwait(false);
        await CompleteStepAsync(item.JobId, "Finding candidate sources", jobToken).ConfigureAwait(false);

        await BeginStepAsync(item.JobId, "Filtering sources", "Filtering candidate sources.", jobToken).ConfigureAwait(false);
        await CompleteStepAsync(item.JobId, "Filtering sources", jobToken).ConfigureAwait(false);

        await BeginStepAsync(item.JobId, "Retrieving context", "Retrieving relevant context for the prompt.", jobToken).ConfigureAwait(false);

        jobToken.ThrowIfCancellationRequested();

        IReadOnlyList<SearchResult>? retrieval;
        var toolInvocationCount = 0;
        var toolName = item.Plan.RequiresToolCall ? item.Plan.ToolName : string.Empty;
        if (item.Plan.RequiresToolCall)
        {
            state.Update("Tool call", $"Invoking repository tool: {toolName}");
            var toolResult = await InvokeToolAsync(item, state, jobToken).ConfigureAwait(false);
            retrieval = toolResult.Results;
            toolInvocationCount = toolResult.InvocationCount;
            if (!string.IsNullOrWhiteSpace(toolResult.Error))
            {
                await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
                {
                    Stage = "Tool call",
                    Message = toolResult.Error
                }, jobToken).ConfigureAwait(false);
            }
            else if (retrieval is { Count: > 0 })
            {
                await _progressStore.SetPartialResultAsync(item.JobId, $"Tool {toolName} returned {retrieval.Count} results.", jobToken).ConfigureAwait(false);
            }
        }
        else if (item.Mode == ChatExecutionMode.FastPath)
        {
            retrieval = await RunFastPathAsync(item, state, jobToken).ConfigureAwait(false);
        }
        else if (IsSmallCorpusRequest(item))
        {
            retrieval = await RunSmallCorpusRetrieveAsync(item, state, jobToken).ConfigureAwait(false);
        }
        else
        {
            retrieval = item.Mode switch
            {
                ChatExecutionMode.CorpusAnalysis => await RunGlobalSearchAsync(item, state, jobToken).ConfigureAwait(false),
                ChatExecutionMode.TimelineAnalysis => await RunGlobalSearchAsync(item, state, jobToken).ConfigureAwait(false),
                ChatExecutionMode.ComparativeAnalysis => await RunRagsRetrieveAsync(item, state, jobToken).ConfigureAwait(false),
                ChatExecutionMode.StructuredSynthesis => await RunRagsRetrieveAsync(item, state, jobToken).ConfigureAwait(false),
                ChatExecutionMode.Retrieval => await RunRagsRetrieveAsync(item, state, jobToken).ConfigureAwait(false),
                _ => Array.Empty<SearchResult>()
            };
        }

        if (state.IsCancelled)
        {
            await _progressStore.FinalizeAsync(item.JobId, ChatJobStatus.Cancelled, null, "Cancelled by user.", jobToken).ConfigureAwait(false);
            return;
        }

        await CompleteStepAsync(item.JobId, "Retrieving context", jobToken).ConfigureAwait(false);

        if (retrieval is { Count: > 0 })
        {
            await BeginStepAsync(item.JobId, "Expanding graph context", "Expanding graph context where available.", jobToken).ConfigureAwait(false);
            await CompleteStepAsync(item.JobId, "Expanding graph context", jobToken).ConfigureAwait(false);
        }
        else
        {
            await MarkStepSkippedAsync(item.JobId, "Expanding graph context", jobToken).ConfigureAwait(false);
        }

        await BeginStepAsync(item.JobId, "Extracting requested facts", "Extracting requested facts from retrieved context.", jobToken).ConfigureAwait(false);
        await _progressStore.SetPartialResultAsync(item.JobId, $"Retrieved {retrieval?.Count ?? 0} context chunks.", jobToken).ConfigureAwait(false);
        await CompleteStepAsync(item.JobId, "Extracting requested facts", jobToken).ConfigureAwait(false);

        await BeginStepAsync(item.JobId, "Validating citations", "Validating citations.", jobToken).ConfigureAwait(false);
        await CompleteStepAsync(item.JobId, "Validating citations", jobToken).ConfigureAwait(false);

        await BeginStepAsync(item.JobId, "Synthesizing answer", "Generating the final response.", jobToken).ConfigureAwait(false);
        state.Update("Synthesis", "Generating the final response.");
        var options = BuildOptions(item.Mode, retrieval);
        llmCallCount++;
        var chatResult = await RunWithHeartbeatAsync(
            item.JobId,
            state,
            "Synthesis",
            "Still generating the final response.",
            ct => _copilotService.ChatAsync(new ChatSession(), item.Prompt, options, ct),
            jobToken).ConfigureAwait(false);

        if (state.IsCancelled)
        {
            await _progressStore.FinalizeAsync(item.JobId, ChatJobStatus.Cancelled, null, "Cancelled by user.", jobToken).ConfigureAwait(false);
            return;
        }

        if (chatResult.IsFailure || chatResult.Value is null)
        {
            var error = chatResult.Error ?? "Chat synthesis failed.";
            await FinalizeFailedAsync(item.JobId, state, error).ConfigureAwait(false);
            return;
        }

        await CompleteStepAsync(item.JobId, "Synthesizing answer", jobToken).ConfigureAwait(false);

        await BeginStepAsync(item.JobId, "Finalizing telemetry", "Recording completion telemetry.", jobToken).ConfigureAwait(false);
        overallStopwatch.Stop();
        var message = chatResult.Value;
        var telemetry = _telemetryService.BuildTelemetry(
            item.JobId,
            item.Plan,
            message.Stats,
            overallStopwatch.Elapsed,
            llmCallCount,
            usedProviderMetrics: true,
            toolName,
            toolInvocationCount);
        var resultText = FormatResultWithTelemetry(message, telemetry);
        state.Succeed(resultText);
        await _progressStore.SetTelemetryAsync(item.JobId, telemetry, jobToken).ConfigureAwait(false);
        await CompleteStepAsync(item.JobId, "Finalizing telemetry", jobToken).ConfigureAwait(false);

        await BeginStepAsync(item.JobId, "Completed", "Execution completed.", jobToken).ConfigureAwait(false);
        await CompleteStepAsync(item.JobId, "Completed", jobToken).ConfigureAwait(false);
        await _progressStore.FinalizeAsync(item.JobId, ChatJobStatus.Succeeded, resultText, null, jobToken).ConfigureAwait(false);
    }

    private static string FormatResultWithTelemetry(ChatMessage message, ChatExecutionTelemetry telemetry)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(message.Content);
        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine($"**Execution telemetry**: {telemetry.EstimateComparisonSummary}");
        builder.AppendLine($"- Elapsed: {telemetry.ElapsedSeconds:F1}s | Prompt tokens: {telemetry.PromptTokens} | Completion tokens: {telemetry.CompletionTokens} | Tokens/sec: {telemetry.TokensPerSecond:F2}");
        builder.AppendLine($"- Retrieval: {telemetry.RetrievalCount} chunks | Citations: {telemetry.CitationCount} | Model calls: {telemetry.LlmCallCount}");
        builder.AppendLine($"- Alignment confidence: {telemetry.AlignmentConfidence:P0} ({telemetry.ConfidenceBasis})");
        if (!string.IsNullOrWhiteSpace(telemetry.ToolName))
        {
            builder.AppendLine($"- Tool: {telemetry.ToolName} | Tool invocations: {telemetry.ToolInvocationCount}");
        }
        return builder.ToString();
    }

    private bool IsSmallCorpusRequest(ChatJobWorkItem item)
    {
        return item.EstimatedRetrievalCount > 0
            && item.EstimatedRetrievalCount <= _options.SmallCorpusDocumentThreshold;
    }

    private async Task<IReadOnlyList<SearchResult>?>? RunFastPathAsync(ChatJobWorkItem item, ChatJobState state, CancellationToken cancellationToken)
    {
        state.Update("Fast path", "Small corpus fast-path retrieval.");
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(TimeSpan.FromSeconds(_options.SmallCorpusTimeoutSeconds));
        var result = await RunWithHeartbeatAsync(
            item.JobId,
            state,
            "RAGS retrieval",
            "Still retrieving relevant chunks.",
            ct => _ragsService.RetrieveAsync(new RetrievalRequest(item.Prompt, item.EstimatedRetrievalCount), ct),
            stepCts.Token).ConfigureAwait(false);

        if (result.IsFailure || result.Value is null || result.Value.Count == 0)
        {
            state.Update("Fast path", result.Error ?? "Fast-path retrieval returned no results.", force: true);
            return Array.Empty<SearchResult>();
        }

        return result.Value;
    }

    private async Task<IReadOnlyList<SearchResult>?>? RunSmallCorpusRetrieveAsync(ChatJobWorkItem item, ChatJobState state, CancellationToken cancellationToken)
    {
        state.Update("RAGS retrieval", $"Small corpus quick-return: retrieving up to {item.EstimatedRetrievalCount} relevant chunks.");
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(TimeSpan.FromSeconds(_options.SmallCorpusTimeoutSeconds));
        var result = await RunWithHeartbeatAsync(
            item.JobId,
            state,
            "RAGS retrieval",
            "Still retrieving relevant chunks.",
            ct => _ragsService.RetrieveAsync(new RetrievalRequest(item.Prompt, item.EstimatedRetrievalCount), ct),
            stepCts.Token).ConfigureAwait(false);

        if (result.IsFailure || result.Value is null)
        {
            state.Update("RAGS retrieval", result.Error ?? "RAGS retrieval returned no results.", force: true);
            await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
            {
                Stage = "RAGS retrieval",
                Message = result.Error ?? "RAGS retrieval returned no results."
            }, cancellationToken).ConfigureAwait(false);
            return Array.Empty<SearchResult>();
        }

        return result.Value.Count == 0 ? Array.Empty<SearchResult>() : result.Value;
    }

    private async Task<IReadOnlyList<SearchResult>?>? RunRagsRetrieveAsync(ChatJobWorkItem item, ChatJobState state, CancellationToken cancellationToken)
    {
        state.Update("RAGS retrieval", $"Retrieving up to {item.EstimatedRetrievalCount} relevant chunks.");
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(TimeSpan.FromSeconds(_options.DefaultStepTimeoutSeconds));
        var result = await RunWithHeartbeatAsync(
            item.JobId,
            state,
            "RAGS retrieval",
            "Still retrieving relevant chunks.",
            ct => _ragsService.RetrieveAsync(new RetrievalRequest(item.Prompt, item.EstimatedRetrievalCount), ct),
            stepCts.Token).ConfigureAwait(false);

        if (result.IsFailure || result.Value is null)
        {
            state.Update("RAGS retrieval", result.Error ?? "RAGS retrieval returned no results.", force: true);
            await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
            {
                Stage = "RAGS retrieval",
                Message = result.Error ?? "RAGS retrieval returned no results."
            }, cancellationToken).ConfigureAwait(false);
            return null;
        }

        return result.Value;
    }

    private async Task<IReadOnlyList<SearchResult>?>? RunGlobalSearchAsync(ChatJobWorkItem item, ChatJobState state, CancellationToken cancellationToken)
    {
        state.Update("Global search", "Running corpus-level community summary search.");
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(TimeSpan.FromSeconds(_options.DefaultStepTimeoutSeconds));

        var graphResult = await RunWithHeartbeatAsync(
            item.JobId,
            state,
            "Global search",
            "Still running GraphRAG global search.",
            ct => _graphRagService.GlobalSearchAsync(item.Prompt, ct),
            stepCts.Token).ConfigureAwait(false);

        if (graphResult.IsSuccess && graphResult.Value is not null)
        {
            var page = graphResult.Value;
            var sourceId = page.Citations.FirstOrDefault() is { } citation && Guid.TryParse(citation, out var parsed) ? parsed : Guid.NewGuid();
            var chunk = new Chunk(Guid.NewGuid(), sourceId, page.Answer, 0);
            return new[] { new SearchResult(chunk, 1.0f, page.Citations, retrievalStrategy: "graphrag-global") };
        }

        var lazyResult = await RunWithHeartbeatAsync(
            item.JobId,
            state,
            "Global search",
            "Still running LazyGraphRAG global search.",
            ct => _lazyGraphRagService.GlobalSearchAsync(item.Prompt, ct),
            stepCts.Token).ConfigureAwait(false);

        if (lazyResult.IsSuccess && lazyResult.Value is not null)
        {
            var page = lazyResult.Value;
            var sourceId = page.Citations.FirstOrDefault() is { } citation && Guid.TryParse(citation, out var parsed) ? parsed : Guid.NewGuid();
            var chunk = new Chunk(Guid.NewGuid(), sourceId, page.Answer, 0);
            return new[] { new SearchResult(chunk, 1.0f, page.Citations, retrievalStrategy: "lazygraphrag-global") };
        }

        state.Update("Global search", $"Global search fallback to RAGS. {graphResult.Error}", force: true);
        await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
        {
            Stage = "Global search",
            Message = $"GraphRAG global search failed. {graphResult.Error}"
        }, cancellationToken).ConfigureAwait(false);
        var fallback = await RunRagsRetrieveAsync(item, state, cancellationToken).ConfigureAwait(false);
        return fallback ?? Array.Empty<SearchResult>();
    }

    private async Task<ToolInvocationResult> InvokeToolAsync(ChatJobWorkItem item, ChatJobState state, CancellationToken cancellationToken)
    {
        var toolName = item.Plan.ToolName;
        var arguments = item.Plan.ToolArguments;
        var query = arguments.TryGetValue("query", out var q) && !string.IsNullOrWhiteSpace(q) ? q : item.Prompt;
        var topK = arguments.TryGetValue("topK", out var tk) && int.TryParse(tk, out var topKValue) ? topKValue : item.EstimatedRetrievalCount;

        state.Update("Tool call", $"Invoking repository tool: {toolName}");
        await BeginStepAsync(item.JobId, "Call repository tool", $"Invoking {toolName}.", cancellationToken).ConfigureAwait(false);

        try
        {
            ToolInvocationResult result;
            if (toolName.Contains("SearchRags", StringComparison.OrdinalIgnoreCase))
            {
                var results = await _ragsService.RetrieveAsync(new RetrievalRequest(query, topK), cancellationToken).ConfigureAwait(false);
                if (results.IsFailure || results.Value is null)
                {
                    result = new ToolInvocationResult(Array.Empty<SearchResult>(), 1, results.Error ?? "RAGS retrieval returned no results.");
                }
                else
                {
                    result = new ToolInvocationResult(results.Value, 1);
                }
            }
            else if (toolName.Contains("SearchGraphRag", StringComparison.OrdinalIgnoreCase))
            {
                var page = await _graphRagService.GlobalSearchAsync(query, cancellationToken).ConfigureAwait(false);
                result = page.IsSuccess && page.Value is not null
                    ? ConvertGlobalSearchToToolResult(page.Value, "graphrag-global")
                    : new ToolInvocationResult(Array.Empty<SearchResult>(), 1, page.Error ?? "GraphRAG global search failed.");
            }
            else if (toolName.Contains("SearchLazyGraphRag", StringComparison.OrdinalIgnoreCase))
            {
                var page = await _lazyGraphRagService.GlobalSearchAsync(query, cancellationToken).ConfigureAwait(false);
                result = page.IsSuccess && page.Value is not null
                    ? ConvertGlobalSearchToToolResult(page.Value, "lazygraphrag-global")
                    : new ToolInvocationResult(Array.Empty<SearchResult>(), 1, page.Error ?? "LazyGraphRAG global search failed.");
            }
            else if (toolName.Contains("SearchGlobalGraph", StringComparison.OrdinalIgnoreCase))
            {
                var page = await _globalGraphSearchService.SearchAsync(query, cancellationToken).ConfigureAwait(false);
                result = page.IsSuccess && page.Value is not null
                    ? ConvertGlobalSearchToToolResult(page.Value, "global-graph")
                    : new ToolInvocationResult(Array.Empty<SearchResult>(), 1, page.Error ?? "Global graph search failed.");
            }
            else
            {
                result = new ToolInvocationResult(Array.Empty<SearchResult>(), 0, $"Unknown tool: {toolName}.");
            }

            await CompleteStepAsync(item.JobId, "Call repository tool", cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool invocation {ToolName} failed for job {JobId}.", toolName, item.JobId);
            await MarkRunningStepFailedAsync(item.JobId, ex.Message, cancellationToken).ConfigureAwait(false);
            return new ToolInvocationResult(Array.Empty<SearchResult>(), 0, $"Tool invocation failed: {ex.Message}");
        }
    }

    private static ToolInvocationResult ConvertGlobalSearchToToolResult(GlobalSearchResult page, string strategy)
    {
        if (string.IsNullOrWhiteSpace(page.Answer))
        {
            return new ToolInvocationResult(Array.Empty<SearchResult>(), 1, "Global search returned an empty answer.");
        }

        var sourceId = page.Citations.FirstOrDefault() is { } citation && Guid.TryParse(citation, out var parsed) ? parsed : Guid.NewGuid();
        var chunk = new Chunk(Guid.NewGuid(), sourceId, page.Answer, 0);
        var results = new[] { new SearchResult(chunk, 1.0f, page.Citations, retrievalStrategy: strategy) };
        return new ToolInvocationResult(results, 1);
    }

    private sealed record ToolInvocationResult(IReadOnlyList<SearchResult> Results, int InvocationCount, string? Error = null);

    private static ChatRequestOptions? BuildOptions(ChatExecutionMode mode, IReadOnlyList<SearchResult>? retrieval)
    {
        return mode switch
        {
            ChatExecutionMode.StructuredSynthesis => new ChatRequestOptions { OutputFormat = "table" },
            _ => null
        };
    }

    private async Task<Result<T>> RunWithHeartbeatAsync<T>(
        Guid jobId,
        ChatJobState state,
        string stage,
        string detail,
        Func<CancellationToken, Task<Result<T>>> operation,
        CancellationToken cancellationToken)
    {
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(SelectHeartbeatInterval(stage));
            while (await timer.WaitForNextTickAsync(heartbeatCts.Token).ConfigureAwait(false))
            {
                if (state.IsCancelled)
                {
                    return;
                }

                state.Update(stage, detail, force: true);
                await _progressStore.AppendHeartbeatAsync(jobId, new ChatProgressHeartbeat
                {
                    Stage = stage,
                    Detail = detail,
                    PercentComplete = state.PercentComplete,
                    Timestamp = DateTimeOffset.UtcNow
                }, heartbeatCts.Token).ConfigureAwait(false);
                state.RecordHeartbeat();
            }
        }, heartbeatCts.Token);

        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await heartbeatCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private TimeSpan SelectHeartbeatInterval(string stage)
    {
        return stage is "Synthesis" or "Global search" or "RAGS retrieval"
            ? TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds)
            : TimeSpan.FromSeconds(_options.LongWaitHeartbeatIntervalSeconds);
    }

    private async Task BeginStepAsync(Guid jobId, string name, string detail, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _progressStore.UpdateStepAsync(jobId, new ChatProgressStep
        {
            Name = name,
            Status = ChatProgressStepStatus.Running,
            Order = ProgressStages.IndexOf(name),
            StartedAt = DateTimeOffset.UtcNow,
            Detail = detail
        }, cancellationToken).ConfigureAwait(false);

        if (_jobs.TryGetValue(jobId, out var state))
        {
            var percent = Math.Clamp((ProgressStages.IndexOf(name) * 100) / Math.Max(1, ProgressStages.Count - 1), 0, 99);
            state.Update(name, detail, percent);
        }
    }

    private async Task CompleteStepAsync(Guid jobId, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _progressStore.UpdateStepAsync(jobId, new ChatProgressStep
        {
            Name = name,
            Status = ChatProgressStepStatus.Completed,
            Order = ProgressStages.IndexOf(name),
            CompletedAt = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);

        if (_jobs.TryGetValue(jobId, out var state))
        {
            var percent = Math.Clamp((ProgressStages.IndexOf(name) * 100) / Math.Max(1, ProgressStages.Count - 1), 0, 99);
            state.Update(name, $"{name} completed.", percent);
        }
    }

    private async Task MarkStepSkippedAsync(Guid jobId, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _progressStore.UpdateStepAsync(jobId, new ChatProgressStep
        {
            Name = name,
            Status = ChatProgressStepStatus.Skipped,
            Order = ProgressStages.IndexOf(name)
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkRunningStepFailedAsync(Guid jobId, string error, CancellationToken cancellationToken)
    {
        var progress = await _progressStore.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (!progress.IsSuccess || progress.Value is null)
        {
            return;
        }

        var runningStep = progress.Value.Steps.FirstOrDefault(s => s.Status == ChatProgressStepStatus.Running);
        if (runningStep is null)
        {
            return;
        }

        await _progressStore.UpdateStepAsync(jobId, new ChatProgressStep
        {
            Name = runningStep.Name,
            Status = ChatProgressStepStatus.Failed,
            Order = runningStep.Order,
            StartedAt = runningStep.StartedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Detail = error
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task FinalizeFailedAsync(Guid jobId, ChatJobState state, string error)
    {
        state.Fail(error);

        try
        {
            using var failCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await MarkRunningStepFailedAsync(jobId, error, failCts.Token).ConfigureAwait(false);
            await _progressStore.FinalizeAsync(jobId, ChatJobStatus.Failed, null, error, failCts.Token).ConfigureAwait(false);
        }
        catch (Exception finalizeEx)
        {
            _logger.LogError(finalizeEx, "Failed to finalize progress record for job {JobId}.", jobId);
        }
    }

    private static ChatProgressRecord CreateProgressRecord(Guid jobId, ChatPlanRecord plan)
    {
        return new ChatProgressRecord
        {
            JobId = jobId,
            PlanId = plan.PlanId,
            Prompt = plan.Prompt,
            Status = ChatJobStatus.Queued,
            Steps = ProgressStages
                .Select((name, index) => new ChatProgressStep
                {
                    Name = name,
                    Status = ChatProgressStepStatus.Pending,
                    Order = index
                })
                .ToList(),
            Heartbeats = Array.Empty<ChatProgressHeartbeat>(),
            Messages = Array.Empty<ChatProgressMessage>(),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private void TrimOldJobs()
    {
        while (_jobs.Count > MaxJobs && _jobOrder.TryDequeue(out var oldJobId))
        {
            if (_jobs.TryGetValue(oldJobId, out var state) && !state.IsActive)
            {
                _jobs.TryRemove(oldJobId, out _);
            }
        }
    }

    private sealed record ChatJobWorkItem(Guid JobId, ChatPlanRecord Plan)
    {
        public string Prompt => Plan.Prompt;

        public ChatExecutionMode Mode => Plan.Mode;

        public int EstimatedRetrievalCount => Plan.EstimatedRetrievalCount;
    }
}
