using System.Collections.Concurrent;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
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
        "Verify tool returned internal context before synthesis",
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
    private readonly IKnowledgeSourceResolver? _knowledgeSourceResolver;
    private readonly IKnowledgeSourceIngestionService? _knowledgeSourceIngestionService;
    private readonly IMetadataRepository? _metadataRepository;
    private readonly IDocumentTemplateRegistry? _templateRegistry;
    private readonly IKnowledgeThemeService? _themeService;
    private readonly IChatToolInvoker _toolInvoker;
    private readonly IChatProgressStore _progressStore;
    private readonly IChatTelemetryService _telemetryService;
    private readonly ChatExecutionEngineOptions _options;
    private readonly ChatAgentOptions _chatAgentOptions;
    private readonly ILogger<ChatExecutionEngine> _logger;
    private readonly ConcurrentDictionary<Guid, SessionMemory> _sessionMemory = new();

    public ChatExecutionEngine(
        IChatPlanApprovalService planApprovalService,
        ICopilotService copilotService,
        IRagsService ragsService,
        IGraphRagService graphRagService,
        ILazyGraphRagService lazyGraphRagService,
        IGlobalGraphSearchService globalGraphSearchService,
        IChatToolInvoker toolInvoker,
        IChatProgressStore progressStore,
        IChatTelemetryService telemetryService,
        IOptions<ChatExecutionEngineOptions> options,
        ILogger<ChatExecutionEngine> logger,
        IOptions<ChatAgentOptions>? chatAgentOptions = null,
        IKnowledgeSourceResolver? knowledgeSourceResolver = null,
        IKnowledgeSourceIngestionService? knowledgeSourceIngestionService = null,
        IMetadataRepository? metadataRepository = null,
        IDocumentTemplateRegistry? templateRegistry = null,
        IKnowledgeThemeService? themeService = null)
    {
        _planApprovalService = planApprovalService ?? throw new ArgumentNullException(nameof(planApprovalService));
        _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));
        _ragsService = ragsService ?? throw new ArgumentNullException(nameof(ragsService));
        _graphRagService = graphRagService ?? throw new ArgumentNullException(nameof(graphRagService));
        _lazyGraphRagService = lazyGraphRagService ?? throw new ArgumentNullException(nameof(lazyGraphRagService));
        _globalGraphSearchService = globalGraphSearchService ?? throw new ArgumentNullException(nameof(globalGraphSearchService));
        _toolInvoker = toolInvoker ?? throw new ArgumentNullException(nameof(toolInvoker));
        _knowledgeSourceResolver = knowledgeSourceResolver;
        _knowledgeSourceIngestionService = knowledgeSourceIngestionService;
        _metadataRepository = metadataRepository;
        _templateRegistry = templateRegistry;
        _themeService = themeService;
        _progressStore = progressStore ?? throw new ArgumentNullException(nameof(progressStore));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _chatAgentOptions = chatAgentOptions?.Value ?? new ChatAgentOptions();
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
        using var concurrency = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentChatJobs));
        var runningJobs = new ConcurrentDictionary<Guid, Task>();

        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                if (!_jobs.TryGetValue(item.JobId, out var state))
                {
                    continue;
                }

                await concurrency.WaitAsync(stoppingToken).ConfigureAwait(false);
                var jobTask = Task.Run(async () =>
                {
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
                    finally
                    {
                        runningJobs.TryRemove(item.JobId, out _);
                        concurrency.Release();
                    }
                }, CancellationToken.None);
                runningJobs[item.JobId] = jobTask;
            }
        }
        finally
        {
            if (!runningJobs.IsEmpty)
            {
                try
                {
                    await Task.WhenAll(runningJobs.Values).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Timed out waiting for {JobCount} chat execution job(s) during shutdown.", runningJobs.Count);
                }
            }

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
            var threshold = SelectWatchdogThreshold();
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
                    if (now - state.LastHeartbeatAt > threshold)
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
        await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
        {
            Stage = "Planning",
            Message = $"Chat request accepted for background execution: {TrimForTrace(item.Prompt)}"
        }, jobToken).ConfigureAwait(false);
        await CompleteStepAsync(item.JobId, "Planning", jobToken).ConfigureAwait(false);

        await BeginStepAsync(item.JobId, "Finding candidate sources", "Finding candidate sources.", jobToken).ConfigureAwait(false);
        await CompleteStepAsync(item.JobId, "Finding candidate sources", jobToken).ConfigureAwait(false);

        await BeginStepAsync(item.JobId, "Filtering sources", "Filtering candidate sources.", jobToken).ConfigureAwait(false);
        await CompleteStepAsync(item.JobId, "Filtering sources", jobToken).ConfigureAwait(false);

        await BeginStepAsync(item.JobId, "Retrieving context", "Retrieving relevant context for the prompt.", jobToken).ConfigureAwait(false);

        IReadOnlyList<SearchResult>? retrieval = null;
        var toolInvocationCount = 0;
        var toolName = item.Plan.RequiresToolCall ? item.Plan.ToolName : string.Empty;

        // Determine how to obtain retrieval based on plan and agent options
        if (item.Plan.RequiresToolCall)
        {
            state.Update("Tool call", $"Invoking repository tool: {toolName}");
            var toolResult = await InvokeToolAsync(item, state, jobToken).ConfigureAwait(false);
            retrieval = toolResult.Results;
            toolInvocationCount = toolResult.InvocationCount;
            if (!string.IsNullOrWhiteSpace(toolResult.EffectiveToolName))
            {
                toolName = toolResult.EffectiveToolName;
            }

            if (!string.IsNullOrWhiteSpace(toolResult.Error))
            {
                await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
                {
                    Stage = "Tool call",
                    Message = toolResult.Error
                }, jobToken).ConfigureAwait(false);
                await FinalizeFailedAsync(item.JobId, state, $"Mandatory repository tool failed: {toolResult.Error}").ConfigureAwait(false);
                return;
            }

            if (retrieval is null || retrieval.Count == 0)
            {
                var noContextMessage = "Mandatory repository tool returned no internal context. The query cannot be answered from the Aletheia Knowledge Estate.";
                await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
                {
                    Stage = "Tool call",
                    Message = noContextMessage
                }, jobToken).ConfigureAwait(false);
                await FinalizeFailedAsync(item.JobId, state, noContextMessage).ConfigureAwait(false);
                return;
            }

            await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
            {
                Stage = "Tool call",
                Message = $"Repository tool verified: {toolName} returned {retrieval.Count} context result(s)."
            }, jobToken).ConfigureAwait(false);
            await _progressStore.SetPartialResultAsync(item.JobId, $"Tool {toolName} returned {retrieval.Count} results.", jobToken).ConfigureAwait(false);

            await BeginStepAsync(item.JobId, "Verify tool returned internal context before synthesis", "Confirmed internal context was retrieved; proceeding to synthesis.", jobToken).ConfigureAwait(false);
            await CompleteStepAsync(item.JobId, "Verify tool returned internal context before synthesis", jobToken).ConfigureAwait(false);
        }
        else if (_chatAgentOptions.BehaviorFlags.RequireRepositoryLookupBeforeAnswer)
        {
            state.Update("Tool call", "ChatAgent behavior requires repository lookup before answer.");
            var toolResult = await InvokeToolAsync(item, state, jobToken).ConfigureAwait(false);
            retrieval = toolResult.Results;
            toolInvocationCount = toolResult.InvocationCount;
            if (!string.IsNullOrWhiteSpace(toolResult.EffectiveToolName))
            {
                toolName = toolResult.EffectiveToolName;
            }

            if (!string.IsNullOrWhiteSpace(toolResult.Error))
            {
                await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
                {
                    Stage = "Tool call",
                    Message = toolResult.Error
                }, jobToken).ConfigureAwait(false);
                await FinalizeFailedAsync(item.JobId, state, $"Repository lookup failed: {toolResult.Error}").ConfigureAwait(false);
                return;
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
                ChatExecutionMode.CorpusAnalysis => await RunSourceAwareBroadAnalysisAsync(item, state, jobToken).ConfigureAwait(false) ?? Array.Empty<SearchResult>(),
                ChatExecutionMode.TimelineAnalysis => await RunSourceAwareBroadAnalysisAsync(item, state, jobToken).ConfigureAwait(false) ?? Array.Empty<SearchResult>(),
                ChatExecutionMode.ComparativeAnalysis => await RunRagsRetrieveAsync(item, state, jobToken).ConfigureAwait(false) ?? Array.Empty<SearchResult>(),
                ChatExecutionMode.StructuredSynthesis => await RunRagsRetrieveAsync(item, state, jobToken).ConfigureAwait(false) ?? Array.Empty<SearchResult>(),
                ChatExecutionMode.Retrieval => await RunRagsRetrieveAsync(item, state, jobToken).ConfigureAwait(false) ?? Array.Empty<SearchResult>(),
                _ => Array.Empty<SearchResult>()
            };
        }

        // Sprint 58: enforce the session knowledge themes on any retrieval that bypassed the
        // engine's own retrieval paths (e.g. the repository-tool path), so no content from
        // excluded documents reaches synthesis.
        if (retrieval is { Count: > 0 })
        {
            var themeScope = await ResolveThemeSourceIdsAsync(item, jobToken).ConfigureAwait(false);
            if (themeScope is not null)
            {
                retrieval = retrieval.Where(result => themeScope.Contains(result.Chunk.SourceId)).ToList();
            }
        }

        // Mark the "Retrieving context" step as completed now that retrieval logic has finished
        await CompleteStepAsync(item.JobId, "Retrieving context", jobToken).ConfigureAwait(false);


        if (state.IsCancelled)
        {
            await _progressStore.FinalizeAsync(item.JobId, ChatJobStatus.Cancelled, null, "Cancelled by user.", jobToken).ConfigureAwait(false);
            return;
        }

    
        if (retrieval is { Count: > 0 })
        {
            await BeginStepAsync(item.JobId, "Expanding graph context", "Expanding graph context where available.", jobToken).ConfigureAwait(false);
            await CompleteStepAsync(item.JobId, "Expanding graph context", jobToken).ConfigureAwait(false);
        }
        else
        {
            await MarkStepSkippedAsync(item.JobId, "Expanding graph context", jobToken).ConfigureAwait(false);
        }

        // Remember the sources resolved for this conversation so follow-ups can be grounded on them.
        if (item.Plan.SessionId is Guid sessionId && retrieval is not null)
        {
            _sessionMemory[sessionId] = new SessionMemory(ExtractRetrievalSources(retrieval), DateTimeOffset.UtcNow);
        }

        await BeginStepAsync(item.JobId, "Extracting requested facts", "Extracting requested facts from retrieved context.", jobToken).ConfigureAwait(false);
        await _progressStore.SetPartialResultAsync(item.JobId, $"Retrieved {retrieval?.Count ?? 0} context chunks.", jobToken).ConfigureAwait(false);
        await CompleteStepAsync(item.JobId, "Extracting requested facts", jobToken).ConfigureAwait(false);

        await BeginStepAsync(item.JobId, "Validating citations", "Validating citations.", jobToken).ConfigureAwait(false);
        await CompleteStepAsync(item.JobId, "Validating citations", jobToken).ConfigureAwait(false);

        if (!item.Plan.RequiresToolCall)
        {
            await BeginStepAsync(item.JobId, "Synthesis", "Generating the final response.", jobToken).ConfigureAwait(false);
        }

        await BeginStepAsync(item.JobId, "Synthesizing answer", "Generating the final response.", jobToken).ConfigureAwait(false);
        state.Update("Synthesis", "Generating the final response.");
        var options = BuildOptions(item, retrieval);
        await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
        {
            Stage = "Synthesis",
            Message = $"Sending request to chat agent with {retrieval?.Count ?? 0} retrieved context chunk(s). Prompt: {TrimForTrace(item.Prompt)}"
        }, jobToken).ConfigureAwait(false);
        llmCallCount++;
        var chatResult = await RunWithHeartbeatAsync(
            item.JobId,
            state,
            "Synthesis",
            "Still generating the final response.",
            ct => _copilotService.ChatAsync(BuildChatSession(item), item.Prompt, options, ct),
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


        var sourceNames = GetSourceNames(retrieval);
        if (sourceNames.Count > 1)
        {
            var mentionedSources = sourceNames
                .Count(name => chatResult.Value.Content.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (mentionedSources < sourceNames.Count)
            {
                await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
                {
                    Stage = "Synthesis",
                    Message = $"Source coverage warning: response mentions {mentionedSources} of {sourceNames.Count} retrieved source name(s)."
                }, jobToken).ConfigureAwait(false);
            }
        }

        await CompleteStepAsync(item.JobId, "Synthesizing answer", jobToken).ConfigureAwait(false);
        if (!item.Plan.RequiresToolCall)
        {
            await CompleteStepAsync(item.JobId, "Synthesis", jobToken).ConfigureAwait(false);
        }

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
        if (!string.IsNullOrWhiteSpace(telemetry.RetrievalStrategy))
        {
            builder.AppendLine($"- Retrieval strategy: {telemetry.RetrievalStrategy}");
        }
        return builder.ToString();
    }

    private static IReadOnlyList<string> GetSourceNames(IReadOnlyList<SearchResult>? retrieval)
    {
        if (retrieval is null || retrieval.Count == 0)
        {
            return Array.Empty<string>();
        }

        return retrieval
            .GroupBy(result => result.Chunk.SourceId)
            .Select(group => group
                .SelectMany(result => result.Citations)
                .FirstOrDefault(citation => !string.IsNullOrWhiteSpace(citation)
                    && !Guid.TryParse(citation.Trim(), out _))
                ?? group.Key.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool IsSmallCorpusRequest(ChatJobWorkItem item)
    {
        var thresholdCount = item.EstimatedRetrievalCount;
        return thresholdCount > 0
            && thresholdCount <= _options.SmallCorpusDocumentThreshold;
    }

    private async Task<IReadOnlyList<SearchResult>> RetrieveSmallCorpusScopedCollectionResultsAsync(
        Guid jobId,
        ChatJobState state,
        string query,
        int topK,
        IReadOnlyList<KnowledgeSource> sources,
        CancellationToken cancellationToken)
    {
        await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
        {
            Stage = "Tool call",
            Message = $"Small corpus fast path: retrieving bounded context from {sources.Count} registered source(s)."
        }, cancellationToken).ConfigureAwait(false);

        var merged = new Dictionary<Guid, SearchResult>();
        var perSourceTopK = Math.Clamp(topK / Math.Max(1, sources.Count), 2, 5);
        var smallCorpusCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        smallCorpusCts.CancelAfter(TimeSpan.FromSeconds(_options.SmallCorpusTimeoutSeconds));

        foreach (var source in sources)
        {
            if (_knowledgeSourceIngestionService is not null)
            {
                await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
                {
                    Stage = "Tool call",
                    Message = $"Hydrating source {source.SourceName} if needed."
                }, cancellationToken).ConfigureAwait(false);

                using var hydrationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                hydrationCts.CancelAfter(TimeSpan.FromSeconds(_options.HydrationTimeoutSeconds));
                try
                {
                    var ingestion = await _knowledgeSourceIngestionService
                        .EnsureIngestedAsync(source, hydrationCts.Token)
                        .ConfigureAwait(false);
                    if (ingestion.IsFailure)
                    {
                        await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
                        {
                            Stage = "Tool call",
                            Message = $"Could not hydrate {source.SourceName}: {ingestion.Error ?? "source ingestion failed."}"
                        }, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
                        {
                            Stage = "Tool call",
                            Message = $"Source {source.SourceName} hydration completed."
                        }, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (hydrationCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
                    {
                        Stage = "Tool call",
                        Message = $"Source {source.SourceName} hydration timed out after {_options.HydrationTimeoutSeconds}s; using already-indexed chunks if available."
                    }, cancellationToken).ConfigureAwait(false);
                }
            }

            var scopedResults = await RunToolRagsRetrieveAsync(
                jobId,
                state,
                query,
                perSourceTopK,
                source.SourceId,
                smallCorpusCts.Token).ConfigureAwait(false);

            if (scopedResults.IsSuccess && scopedResults.Value is not null)
            {
                foreach (var result in scopedResults.Value)
                {
                    merged.TryAdd(result.Chunk.Id, result);
                }
            }
        }

        return merged.Values
            .OrderByDescending(result => result.Score)
            .Take(Math.Max(topK, sources.Count))
            .ToList();
    }

    private async Task<IReadOnlyList<SearchResult>> RunFastPathAsync(ChatJobWorkItem item, ChatJobState state, CancellationToken cancellationToken)
    {
        state.Update("Fast path", "Small corpus fast-path retrieval.");
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(TimeSpan.FromSeconds(_options.SmallCorpusTimeoutSeconds));
        var topK = Math.Max(1, item.EstimatedRetrievalCount);
        var scopedRetrieval = await TrySourceScopedRetrievalAsync(item, state, topK, cancellationToken).ConfigureAwait(false);
        if (scopedRetrieval is not null)
        {
            return scopedRetrieval;
        }
        var themeSourceIds = await ResolveThemeSourceIdsAsync(item, cancellationToken).ConfigureAwait(false);
        var result = await RunWithHeartbeatAsync(
            item.JobId,
            state,
            "RAGS retrieval",
            "Still retrieving relevant chunks.",
            ct => _ragsService.RetrieveAsync(new RetrievalRequest(item.Prompt, topK, sourceIds: themeSourceIds), ct),
            stepCts.Token).ConfigureAwait(false);

        if (result.IsFailure || result.Value is null || result.Value.Count == 0)
        {
            state.Update("Fast path", result.Error ?? "Fast-path retrieval returned no results.", force: true);
            return Array.Empty<SearchResult>();
        }

        return result.Value;
    }

    private async Task<IReadOnlyList<SearchResult>> RunSmallCorpusRetrieveAsync(ChatJobWorkItem item, ChatJobState state, CancellationToken cancellationToken)
    {
        var topK = Math.Max(1, item.EstimatedRetrievalCount);
        var scopedRetrieval = await TrySourceScopedRetrievalAsync(item, state, topK, cancellationToken).ConfigureAwait(false);
        if (scopedRetrieval is not null)
        {
            return scopedRetrieval;
        }
        state.Update("RAGS retrieval", $"Small corpus quick-return: retrieving up to {topK} relevant chunks.");
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(TimeSpan.FromSeconds(_options.SmallCorpusTimeoutSeconds));
        var themeSourceIds = await ResolveThemeSourceIdsAsync(item, cancellationToken).ConfigureAwait(false);
        var result = await RunWithHeartbeatAsync(
            item.JobId,
            state,
            "RAGS retrieval",
            "Still retrieving relevant chunks.",
            ct => _ragsService.RetrieveAsync(new RetrievalRequest(item.Prompt, topK, sourceIds: themeSourceIds), ct),
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

    private async Task<IReadOnlyList<SearchResult>?> RunRagsRetrieveAsync(ChatJobWorkItem item, ChatJobState state, CancellationToken cancellationToken)
    {
        var topK = Math.Max(1, item.EstimatedRetrievalCount);
        state.Update("RAGS retrieval", $"Retrieving up to {topK} relevant chunks.");
        var scopedRetrieval = await TrySourceScopedRetrievalAsync(item, state, topK, cancellationToken).ConfigureAwait(false);
        if (scopedRetrieval is not null)
        {
            return scopedRetrieval;
        }
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(TimeSpan.FromSeconds(_options.DefaultStepTimeoutSeconds));
        var themeSourceIds = await ResolveThemeSourceIdsAsync(item, cancellationToken).ConfigureAwait(false);
        var request = new RetrievalRequest(item.Prompt, topK, sourceIds: themeSourceIds);
        var result = await RunWithHeartbeatAsync(
            item.JobId,
            state,
            "RAGS retrieval",
            "Still retrieving relevant chunks.",
            ct => _ragsService.RetrieveAsync(request, ct),
            stepCts.Token).ConfigureAwait(false);

        if (result.IsFailure || result.Value is null)
        {
            state.Update("RAGS retrieval", result.Error ?? "RAGS retrieval returned no results.", force: true);
            await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
            {
                Stage = "RAGS retrieval",
                Message = result.Error ?? "RAGS retrieval returned no results."
            }, cancellationToken).ConfigureAwait(false);
            return item.Plan.RequiresToolCall ? Array.Empty<SearchResult>() : null;
        }

        return result.Value;
    }

    private async Task<IReadOnlyList<SearchResult>?> RunGlobalSearchAsync(ChatJobWorkItem item, ChatJobState state, CancellationToken cancellationToken)
    {
        state.Update("Global search", "Running GraphRAG global search across repository summaries.");
        await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
        {
            Stage = "Global search",
            Message = "GraphRAG global search is active for this broad repository request."
        }, cancellationToken).ConfigureAwait(false);

        using var graphCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        graphCts.CancelAfter(TimeSpan.FromSeconds(_options.DefaultStepTimeoutSeconds));
        Result<GlobalSearchResult> graph;
        try
        {
            graph = await RunWithHeartbeatAsync(
                item.JobId,
                state,
                "Global search",
                "Still reading GraphRAG summaries.",
                ct => _graphRagService.GlobalSearchAsync(item.Prompt, ct),
                graphCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (graphCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            graph = Result<GlobalSearchResult>.Failure($"GraphRAG global search timed out after {_options.DefaultStepTimeoutSeconds} seconds.");
        }

        if (graph.IsSuccess && graph.Value is not null)
        {
            var graphResult = ConvertGlobalSearchToToolResult(graph.Value, "graphrag-global");
            if (graphResult.Results.Count > 0)
            {
                await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
                {
                    Stage = "Global search",
                    Message = $"GraphRAG global search returned {graphResult.Results.Count} context result(s)."
                }, cancellationToken).ConfigureAwait(false);
                return graphResult.Results;
            }
        }

        var graphError = graph.Error ?? "GraphRAG global search returned no usable context.";
        state.Update("Global search", $"{graphError} Trying LazyGraphRAG.");
        await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
        {
            Stage = "Global search",
            Message = $"{graphError} Trying LazyGraphRAG global search."
        }, cancellationToken).ConfigureAwait(false);

        using var lazyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lazyCts.CancelAfter(TimeSpan.FromSeconds(_options.DefaultStepTimeoutSeconds));
        Result<GlobalSearchResult> lazy;
        try
        {
            lazy = await RunWithHeartbeatAsync(
                item.JobId,
                state,
                "Global search",
                "Still traversing LazyGraphRAG context.",
                ct => _lazyGraphRagService.GlobalSearchAsync(item.Prompt, ct),
                lazyCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lazyCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            lazy = Result<GlobalSearchResult>.Failure($"LazyGraphRAG global search timed out after {_options.DefaultStepTimeoutSeconds} seconds.");
        }

        if (lazy.IsSuccess && lazy.Value is not null)
        {
            var lazyResult = ConvertGlobalSearchToToolResult(lazy.Value, "lazygraphrag-global");
            if (lazyResult.Results.Count > 0)
            {
                await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
                {
                    Stage = "Global search",
                    Message = $"LazyGraphRAG global search returned {lazyResult.Results.Count} context result(s)."
                }, cancellationToken).ConfigureAwait(false);
                return lazyResult.Results;
            }
        }

        var lazyError = lazy.Error ?? "LazyGraphRAG global search returned no usable context.";
        state.Update("Global search", $"{lazyError} Falling back to Semantic RAGS.");
        await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
        {
            Stage = "Global search",
            Message = $"{lazyError} Falling back to Semantic RAGS retrieval."
        }, cancellationToken).ConfigureAwait(false);

        var fallback = await RunRagsRetrieveAsync(item, state, cancellationToken).ConfigureAwait(false);
        return fallback ?? Array.Empty<SearchResult>();
    }

    private async Task<ToolInvocationResult> InvokeToolAsync(ChatJobWorkItem item, ChatJobState state, CancellationToken cancellationToken)
    {
        var toolName = item.Plan.RequiresToolCall && !string.IsNullOrWhiteSpace(item.Plan.ToolName)
            ? item.Plan.ToolName
            : _chatAgentOptions.ToolNames.SearchRepository;
        var arguments = item.Plan.ToolArguments;
        var query = arguments.TryGetValue("query", out var q) && !string.IsNullOrWhiteSpace(q) ? q : item.Prompt;
        var topK = arguments.TryGetValue("topK", out var tk) && int.TryParse(tk, out var topKValue)
            ? Math.Max(1, topKValue)
            : Math.Max(1, item.EstimatedRetrievalCount);

        state.Update("Tool call", $"Invoking repository tool: {toolName}");
        await BeginStepAsync(item.JobId, "Call repository tool", $"Invoking {toolName}.", cancellationToken).ConfigureAwait(false);

        try
        {
            using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var toolTimeoutSeconds = Math.Max(_options.DefaultStepTimeoutSeconds, _options.MandatoryToolTimeoutSeconds);
            stepCts.CancelAfter(TimeSpan.FromSeconds(toolTimeoutSeconds));
            var toolCancellationToken = stepCts.Token;

            var coreResult = await RunWithHeartbeatAsync(
                item.JobId,
                state,
                "Tool call",
                $"Invoking mandatory repository tool: {toolName}",
                async ct => Result<ToolInvocationResult>.Success(await InvokeToolCoreAsync(item, state, toolName, query, topK, ct).ConfigureAwait(false)),
                toolCancellationToken).ConfigureAwait(false);

            if (coreResult.IsFailure || coreResult.Value is null)
            {
                var error = coreResult.Error ?? "Tool invocation returned no result.";
                await MarkStepFailedAsync(item.JobId, "Call repository tool", error, cancellationToken).ConfigureAwait(false);
                return new ToolInvocationResult(Array.Empty<SearchResult>(), 0, error);
            }

            return coreResult.Value;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var toolTimeoutSeconds = Math.Max(_options.DefaultStepTimeoutSeconds, _options.MandatoryToolTimeoutSeconds);
            var error = $"Tool invocation {toolName} timed out after {toolTimeoutSeconds} seconds.";
            _logger.LogWarning("Tool invocation {ToolName} timed out for job {JobId}.", toolName, item.JobId);
            await MarkStepFailedAsync(item.JobId, "Call repository tool", error, cancellationToken).ConfigureAwait(false);
            return new ToolInvocationResult(Array.Empty<SearchResult>(), 0, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool invocation {ToolName} failed for job {JobId}.", toolName, item.JobId);
            await MarkStepFailedAsync(item.JobId, "Call repository tool", ex.Message, cancellationToken).ConfigureAwait(false);
            return new ToolInvocationResult(Array.Empty<SearchResult>(), 0, $"Tool invocation failed: {ex.Message}");
        }
    }

    private async Task<ToolInvocationResult> InvokeToolCoreAsync(
        ChatJobWorkItem item,
        ChatJobState state,
        string toolName,
        string query,
        int topK,
        CancellationToken cancellationToken)
    {
        await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
        {
            Stage = "Tool call",
            Message = $"Dispatching to registered plugin: {toolName}."
        }, cancellationToken).ConfigureAwait(false);

        var effectiveToolName = toolName;

        if (string.IsNullOrWhiteSpace(toolName) && _chatAgentOptions.BehaviorFlags.RequireRepositoryLookupBeforeAnswer)
        {
            effectiveToolName = _chatAgentOptions.ToolNames.SearchRepository;
        }

        if (!string.Equals(effectiveToolName, toolName, StringComparison.OrdinalIgnoreCase))
        {
            await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
            {
                Stage = "Tool call",
                Message = $"Tool {toolName} is not active for normal chat; using {effectiveToolName}."
            }, cancellationToken).ConfigureAwait(false);
        }

        var invocationCount = 0;
        var merged = new Dictionary<Guid, SearchResult>();
        var supportsSourceScopedRetrieval = effectiveToolName.EndsWith(".SearchRags", StringComparison.OrdinalIgnoreCase);

        // Enforce source scoping: when the prompt resolves to registered source(s), skip the broad pass and
        // invoke the tool once per matching source. A single clear winner keeps the answer inside one document;
        // list/summary prompts retrieve each matching document independently.
        var scopedSources = supportsSourceScopedRetrieval
            ? (await ResolvePromptSourceScopeAsync(query, cancellationToken, GetPriorSources(item)).ConfigureAwait(false))?.Sources ?? Array.Empty<KnowledgeSource>()
            : Array.Empty<KnowledgeSource>();

        if (scopedSources.Count > 0)
        {
            var perSourceTopK = Math.Clamp(topK / Math.Max(1, scopedSources.Count), 2, 5);
            foreach (var source in scopedSources.Take(25))
            {
                // Template documents: always surface the opening chunks (nature/purpose) up front.
                if (_templateRegistry?.TryGetSections(source.SourceName) is { Count: > 0 })
                {
                    var opening = await _ragsService.RetrieveSourceChunksAsync(source.SourceId, 3, cancellationToken).ConfigureAwait(false);
                    if (opening.IsSuccess && opening.Value is not null)
                    {
                        foreach (var result in opening.Value)
                        {
                            merged.TryAdd(result.Chunk.Id, result);
                        }
                    }
                }

                var arguments = BuildToolArguments(query, perSourceTopK, source.SourceId);
                var response = await _toolInvoker.InvokeAsync(effectiveToolName, arguments, cancellationToken).ConfigureAwait(false);
                invocationCount += response.InvocationCount;

                if (response.IsSuccess && response.Results.Count > 0)
                {
                    foreach (var result in response.Results)
                    {
                        merged.TryAdd(result.Chunk.Id, result);
                    }
                }
            }

            if (merged.Count > 0)
            {
                var scopedResults = merged.Values
                    .OrderByDescending(result => result.Score)
                    .Take(Math.Max(topK, scopedSources.Count))
                    .ToList();

                await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
                {
                    Stage = "Tool call",
                    Message = $"Plugin {effectiveToolName} returned {scopedResults.Count} scoped result(s) from {scopedSources.Count} source(s)."
                }, cancellationToken).ConfigureAwait(false);

                await CompleteStepAsync(item.JobId, "Call repository tool", cancellationToken).ConfigureAwait(false);
                return new ToolInvocationResult(scopedResults, invocationCount, null, effectiveToolName);
            }
        }

        var broadArguments = BuildToolArguments(query, topK, null);
        var broadResponse = await _toolInvoker.InvokeAsync(effectiveToolName, broadArguments, cancellationToken).ConfigureAwait(false);
        invocationCount += broadResponse.InvocationCount;

        if (!broadResponse.IsSuccess)
        {
            _logger.LogWarning("Chat tool {ToolName} returned error for job {JobId}: {Error}. Falling back to scoped RAGS retrieval.", effectiveToolName, item.JobId, broadResponse.Error);
            await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
            {
                Stage = "Tool call",
                Message = $"Plugin {effectiveToolName} returned no usable context ({broadResponse.Error}); running scoped fallback."
            }, cancellationToken).ConfigureAwait(false);

            var fallback = await FallbackToRagsToolAsync(
                item.JobId,
                state,
                query,
                topK,
                effectiveToolName,
                broadResponse.Error ?? "Plugin returned no results.",
                cancellationToken).ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(fallback.Error)
                ? fallback with { EffectiveToolName = effectiveToolName, InvocationCount = invocationCount + fallback.InvocationCount }
                : fallback;
        }

        if (broadResponse.Results.Count == 0)
        {
            _logger.LogInformation("Chat tool {ToolName} returned no results for job {JobId}; running scoped fallback.", effectiveToolName, item.JobId);
            await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
            {
                Stage = "Tool call",
                Message = $"Plugin {effectiveToolName} returned no results; running scoped fallback."
            }, cancellationToken).ConfigureAwait(false);

            var fallback = await FallbackToRagsToolAsync(
                item.JobId,
                state,
                query,
                topK,
                effectiveToolName,
                "Plugin returned no results.",
                cancellationToken).ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(fallback.Error)
                ? fallback with { EffectiveToolName = effectiveToolName, InvocationCount = invocationCount + fallback.InvocationCount }
                : fallback;
        }

        await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
        {
            Stage = "Tool call",
            Message = $"Plugin {effectiveToolName} returned {broadResponse.Results.Count} result(s)."
        }, cancellationToken).ConfigureAwait(false);

        await CompleteStepAsync(item.JobId, "Call repository tool", cancellationToken).ConfigureAwait(false);
        return new ToolInvocationResult(broadResponse.Results, invocationCount, null, effectiveToolName);
    }

    private static IReadOnlyDictionary<string, string> BuildToolArguments(string query, int topK, Guid? sourceId)
    {
        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["query"] = query,
            ["topK"] = topK.ToString(CultureInfo.InvariantCulture)
        };

        if (sourceId.HasValue)
        {
            arguments["sourceId"] = sourceId.Value.ToString();
        }

        return arguments;
    }

    private static string TrimForTrace(string value)
    {
        const int maxLength = 240;
        var compact = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return compact.Length <= maxLength ? compact : $"{compact[..maxLength]}...";
    }

    private async Task<ToolInvocationResult> FallbackToRagsToolAsync(
        Guid jobId,
        ChatJobState state,
        string query,
        int topK,
        string failedToolName,
        string reason,
        CancellationToken cancellationToken)
    {
        var message = $"{failedToolName} could not return usable context: {reason} Falling back to AletheiaKnowledgePlugin.SearchRags.";
        state.Update("Tool call", message, force: true);
        await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
        {
            Stage = "Tool call",
            Message = message
        }, cancellationToken).ConfigureAwait(false);

        var queryVariants = BuildMandatoryFallbackQueries(query);

        // If metadata already identifies matching sources, prefer per-source hydration/search over a broad repository pass.
        var fallbackSources = await ResolveFallbackSourcesAsync(jobId, query, cancellationToken).ConfigureAwait(false);
        if (fallbackSources.Count > 0)
        {
            return await RetrieveScopedSourceFallbackAsync(
                jobId,
                state,
                query,
                topK,
                fallbackSources,
                queryVariants,
                cancellationToken).ConfigureAwait(false);
        }

        var fallback = await RetrieveWithQueryVariantsAsync(
            jobId,
            state,
            queryVariants,
            Math.Max(1, topK),
            null,
            cancellationToken).ConfigureAwait(false);
        var scopedFallback = await ExpandScopedCollectionResultsAsync(
            jobId,
            state,
            query,
            topK,
            fallback.Results,
            cancellationToken).ConfigureAwait(false);
        if (scopedFallback.Count > 0)
        {
            return new ToolInvocationResult(
                scopedFallback,
                2,
                null,
                "AletheiaKnowledgePlugin.SearchRags");
        }

        if (fallback.Results.Count > 0)
        {
            return new ToolInvocationResult(
                fallback.Results
                    .Take(Math.Max(1, topK))
                    .ToList(),
                2,
                null,
                "AletheiaKnowledgePlugin.SearchRags");
        }

        if (fallback.Error is not null)
        {
            return new ToolInvocationResult(
                Array.Empty<SearchResult>(),
                2,
                $"{reason}; RAGS fallback failed: {fallback.Error}",
                "AletheiaKnowledgePlugin.SearchRags");
        }

        return new ToolInvocationResult(
            Array.Empty<SearchResult>(),
            2,
            $"{reason}; RAGS fallback returned no internal context after query variants and registered-source hydration.",
            "AletheiaKnowledgePlugin.SearchRags");
    }

    private async Task<ToolInvocationResult> RetrieveScopedSourceFallbackAsync(
        Guid jobId,
        ChatJobState state,
        string query,
        int topK,
        IReadOnlyList<KnowledgeSource> sources,
        IReadOnlyList<string> queryVariants,
        CancellationToken cancellationToken)
    {
        await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
        {
            Stage = "Tool call",
            Message = $"RAGS fallback: hydrating/searching {sources.Count} matching registered source(s)."
        }, cancellationToken).ConfigureAwait(false);

        var sourceResults = new Dictionary<Guid, SearchResult>();
        foreach (var source in sources.Take(10))
        {
            var scopedResults = await RetrieveWithQueryVariantsAsync(
                jobId,
                state,
                queryVariants,
                Math.Max(1, topK),
                source.SourceId,
                cancellationToken).ConfigureAwait(false);

            if (scopedResults.Results.Count > 0)
            {
                await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
                {
                    Stage = "Tool call",
                    Message = $"Found {scopedResults.Results.Count} already-indexed chunk(s) for {source.SourceName}; hydration skipped."
                }, cancellationToken).ConfigureAwait(false);

                foreach (var result in scopedResults.Results)
                {
                    sourceResults.TryAdd(result.Chunk.Id, result);
                }

                continue;
            }

            if (_knowledgeSourceIngestionService is not null)
            {
                if (scopedResults.Error is not null)
                {
                    await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
                    {
                        Stage = "Tool call",
                        Message = $"Already-indexed search for {source.SourceName} returned no usable context ({scopedResults.Error}); attempting bounded hydration."
                    }, cancellationToken).ConfigureAwait(false);
                }

                await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
                {
                    Stage = "Tool call",
                    Message = $"Hydrating fallback source {source.SourceName} if needed."
                }, cancellationToken).ConfigureAwait(false);

                var ingestion = await EnsureSourceHydratedWithTimeoutAsync(
                    jobId,
                    source,
                    "fallback",
                    cancellationToken).ConfigureAwait(false);
                if (ingestion)
                {
                    scopedResults = await RetrieveWithQueryVariantsAsync(
                        jobId,
                        state,
                        queryVariants,
                        Math.Max(1, topK),
                        source.SourceId,
                        cancellationToken).ConfigureAwait(false);

                    foreach (var result in scopedResults.Results)
                    {
                        sourceResults.TryAdd(result.Chunk.Id, result);
                    }
                }
            }
        }

        if (sourceResults.Count > 0)
        {
            return new ToolInvocationResult(
                sourceResults.Values
                    .OrderByDescending(result => result.Score)
                    .Take(Math.Max(1, topK))
                    .ToList(),
                2,
                null,
                "AletheiaKnowledgePlugin.SearchRags");
        }

        var metadataResults = BuildRegisteredSourceMetadataResults(sources);
        if (metadataResults.Count > 0)
        {
            await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
            {
                Stage = "Tool call",
                Message = $"RAGS chunks were not available for the matching registered source(s); using {metadataResults.Count} repository metadata record(s) as bounded internal context."
            }, cancellationToken).ConfigureAwait(false);

            return new ToolInvocationResult(
                metadataResults,
                2,
                null,
                "AletheiaKnowledgePlugin.SearchRags");
        }

        return new ToolInvocationResult(
            Array.Empty<SearchResult>(),
            2,
            "RAGS fallback returned no internal context from matching registered sources.",
            "AletheiaKnowledgePlugin.SearchRags");
    }

    private async Task<RetrievalAttempt> RetrieveWithQueryVariantsAsync(
        Guid jobId,
        ChatJobState state,
        IReadOnlyList<string> queries,
        int topK,
        Guid? sourceId,
        CancellationToken cancellationToken)
    {
        string? lastError = null;
        foreach (var query in queries)
        {
            var timeout = TimeSpan.FromSeconds(sourceId.HasValue
                ? Math.Max(1, _options.SmallCorpusTimeoutSeconds)
                : Math.Max(1, _options.DefaultStepTimeoutSeconds));
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(timeout);

            Result<IReadOnlyList<SearchResult>> result;
            try
            {
                result = await RunToolRagsRetrieveAsync(jobId, state, query, topK, sourceId, attemptCts.Token)
                    .WaitAsync(timeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                lastError = sourceId.HasValue
                    ? $"Source-scoped vector search timed out after {timeout.TotalSeconds:F0}s for source {sourceId.Value}."
                    : $"Vector search timed out after {timeout.TotalSeconds:F0}s.";
                await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
                {
                    Stage = "Tool call",
                    Message = lastError
                }, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (OperationCanceledException) when (attemptCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                lastError = sourceId.HasValue
                    ? $"Source-scoped vector search cancelled after {timeout.TotalSeconds:F0}s for source {sourceId.Value}."
                    : $"Vector search cancelled after {timeout.TotalSeconds:F0}s.";
                await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
                {
                    Stage = "Tool call",
                    Message = lastError
                }, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (result.IsFailure)
            {
                lastError = result.Error;
                continue;
            }

            if (result.Value is { Count: > 0 })
            {
                return new RetrievalAttempt(result.Value, null);
            }
        }

        return new RetrievalAttempt(Array.Empty<SearchResult>(), lastError);
    }

    private async Task<bool> EnsureSourceHydratedWithTimeoutAsync(
        Guid jobId,
        KnowledgeSource source,
        string label,
        CancellationToken cancellationToken)
    {
        if (_knowledgeSourceIngestionService is null)
        {
            return false;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.HydrationTimeoutSeconds));
        using var hydrationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        hydrationCts.CancelAfter(timeout);

        try
        {
            var ingestion = await _knowledgeSourceIngestionService
                .EnsureIngestedAsync(source, hydrationCts.Token)
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            if (ingestion.IsFailure)
            {
                await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
                {
                    Stage = "Tool call",
                    Message = $"Could not hydrate {source.SourceName}: {ingestion.Error ?? "source ingestion failed."}"
                }, cancellationToken).ConfigureAwait(false);
                return false;
            }

            await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
            {
                Stage = "Tool call",
                Message = $"Source {source.SourceName} {label} hydration completed."
            }, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
            {
                Stage = "Tool call",
                Message = $"Source {source.SourceName} {label} hydration timed out after {timeout.TotalSeconds:F0}s; using already-indexed chunks if available."
            }, cancellationToken).ConfigureAwait(false);
            return false;
        }
        catch (OperationCanceledException) when (hydrationCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
            {
                Stage = "Tool call",
                Message = $"Source {source.SourceName} {label} hydration timed out after {timeout.TotalSeconds:F0}s; using already-indexed chunks if available."
            }, cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    private async Task<IReadOnlyList<SearchResult>> ExpandScopedCollectionResultsAsync(
        Guid jobId,
        ChatJobState state,
        string query,
        int topK,
        IReadOnlyList<SearchResult> existingResults,
        CancellationToken cancellationToken)
    {
        if (!IsScopedCollectionPrompt(query))
        {
            return existingResults;
        }

        var sources = await ResolveFallbackSourcesAsync(jobId, query, cancellationToken).ConfigureAwait(false);
        if (sources.Count == 0)
        {
            return existingResults;
        }

        return await RetrieveScopedCollectionResultsAsync(
            jobId,
            state,
            query,
            topK,
            sources,
            existingResults,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SearchResult>> RetrieveScopedCollectionResultsAsync(
        Guid jobId,
        ChatJobState state,
        string query,
        int topK,
        IReadOnlyList<KnowledgeSource> sources,
        IReadOnlyList<SearchResult> existingResults,
        CancellationToken cancellationToken)
    {
        await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
        {
            Stage = "Tool call",
            Message = $"Lazy scoped WRAGS coverage: found {sources.Count} matching registered source(s); retrieving bounded context per source."
        }, cancellationToken).ConfigureAwait(false);

        var merged = new Dictionary<Guid, SearchResult>();
        foreach (var result in existingResults)
        {
            merged.TryAdd(result.Chunk.Id, result);
        }

        var queryVariants = BuildMandatoryFallbackQueries(query);
        if (HasCollectionIntent(query))
        {
            queryVariants = queryVariants
                .Append("project summary purpose objective scope of work overview")
                .ToList();
        }

        var perSourceTopK = Math.Clamp(
            topK / Math.Max(1, sources.Count),
            HasCollectionIntent(query) ? 3 : 2,
            HasCollectionIntent(query) ? 6 : 5);
        foreach (var source in sources.Take(25))
        {
            if (_knowledgeSourceIngestionService is not null)
            {
                await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
                {
                    Stage = "Tool call",
                    Message = $"Hydrating source {source.SourceName} if needed."
                }, cancellationToken).ConfigureAwait(false);

                using var hydrationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                hydrationCts.CancelAfter(TimeSpan.FromSeconds(_options.HydrationTimeoutSeconds));
                try
                {
                    var ingestion = await _knowledgeSourceIngestionService
                        .EnsureIngestedAsync(source, hydrationCts.Token)
                        .ConfigureAwait(false);
                    if (ingestion.IsFailure)
                    {
                        await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
                        {
                            Stage = "Tool call",
                            Message = $"Could not hydrate {source.SourceName}: {ingestion.Error ?? "source ingestion failed."}"
                        }, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
                        {
                            Stage = "Tool call",
                            Message = $"Source {source.SourceName} hydration completed."
                        }, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (hydrationCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
                    {
                        Stage = "Tool call",
                        Message = $"Source {source.SourceName} hydration timed out after {_options.HydrationTimeoutSeconds}s; using already-indexed chunks if available."
                    }, cancellationToken).ConfigureAwait(false);
                }
            }

            var sections = _templateRegistry?.TryGetSections(source.SourceName);
            if (sections is { Count: > 0 })
            {
                // Opening chunks first: the document's nature/purpose lives at the start of the document.
                var opening = await _ragsService
                    .RetrieveSourceChunksAsync(source.SourceId, 3, cancellationToken)
                    .ConfigureAwait(false);
                if (opening.IsSuccess && opening.Value is not null)
                {
                    foreach (var result in opening.Value)
                    {
                        merged.TryAdd(result.Chunk.Id, result);
                    }
                }

                // Per-section scoped evidence following the template's ordered sections.
                foreach (var section in sections.Take(6))
                {
                    var sectionAttempt = await RetrieveWithQueryVariantsAsync(
                        jobId,
                        state,
                        new[] { section.Title },
                        2,
                        source.SourceId,
                        cancellationToken).ConfigureAwait(false);
                    foreach (var result in sectionAttempt.Results)
                    {
                        merged.TryAdd(result.Chunk.Id, result);
                    }
                }
            }
            else
            {
                var scopedResults = await RetrieveWithQueryVariantsAsync(
                    jobId,
                    state,
                    queryVariants,
                    perSourceTopK,
                    source.SourceId,
                    cancellationToken).ConfigureAwait(false);
                foreach (var result in scopedResults.Results)
                {
                    merged.TryAdd(result.Chunk.Id, result);
                }
            }
        }

        return merged.Values
            .OrderBy(result => result.Chunk.SourceId)
            .ThenBy(result => result.Chunk.Index)
            .Take(Math.Max(topK, sources.Count * 12))
            .ToList();
    }

    private async Task<Result<IReadOnlyList<SearchResult>>> RunToolRagsRetrieveAsync(
        Guid jobId,
        ChatJobState state,
        string query,
        int topK,
        Guid? sourceId,
        CancellationToken cancellationToken)
    {
        var detail = sourceId.HasValue
            ? "Still retrieving scoped WRAGS chunks from a registered source."
            : "Still retrieving WRAGS repository chunks for the mandatory tool call.";

        await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
        {
            Stage = "Tool call",
            Message = sourceId.HasValue
                ? $"Querying vector store for source {sourceId.Value}."
                : "Querying vector store for the mandatory tool call."
        }, cancellationToken).ConfigureAwait(false);

        return await _ragsService.RetrieveAsync(new RetrievalRequest(query, topK, sourceId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<KnowledgeSource>> ResolveFallbackSourcesAsync(Guid jobId, string query, CancellationToken cancellationToken)
    {
        await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
        {
            Stage = "Tool call",
            Message = "Resolving matching registered sources from repository metadata."
        }, cancellationToken).ConfigureAwait(false);

        var sources = new Dictionary<Guid, KnowledgeSource>();

        if (_metadataRepository is not null)
        {
            var scope = await ResolvePromptSourceScopeAsync(query, cancellationToken).ConfigureAwait(false);
            if (scope is not null)
            {
                foreach (var source in scope.Sources)
                {
                    sources.TryAdd(source.SourceId, source);
                }
            }
        }

        if (sources.Count == 0 && _knowledgeSourceResolver is not null)
        {
            var resolved = await _knowledgeSourceResolver.ResolveAsync(query, cancellationToken).ConfigureAwait(false);
            if (resolved.IsSuccess && resolved.Value is not null)
            {
                sources.TryAdd(resolved.Value.SourceId, resolved.Value);
            }
        }

        await _progressStore.AppendMessageAsync(jobId, new ChatProgressMessage
        {
            Stage = "Tool call",
            Message = sources.Count == 0
                ? "No matching registered sources found; falling back to broad vector search."
                : $"Resolved {sources.Count} matching registered source(s)."
        }, cancellationToken).ConfigureAwait(false);

        return sources.Values.ToList();
    }

    private sealed record PromptSourceScope(IReadOnlyList<KnowledgeSource> Sources, bool IsSingle);

    private static readonly HashSet<string> PromptStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "above", "all", "an", "and", "are", "artifact", "artifacts", "as", "at", "be", "been",
        "but", "by", "can", "could", "detail", "details", "document", "documents", "docx", "each", "file",
        "files", "for", "from", "give", "have", "in", "into", "is", "it", "its", "last", "list", "me",
        "most", "of", "on", "or", "overview", "past", "pdf", "please", "project", "projects", "provide",
        "provides", "providing", "registered", "rfp", "rfps", "should", "summaries", "summarize",
        "summary", "the", "their", "then", "there", "these", "they", "to", "what", "which", "with"
    };

    private async Task<PromptSourceScope?> ResolvePromptSourceScopeAsync(string query, CancellationToken cancellationToken, IReadOnlyList<KnowledgeSource>? priorSources = null)
    {
        if (_metadataRepository is null || string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var terms = SignificantQueryTerms(query);
        if (terms.Count == 0)
        {
            var generic = await ResolveGenericRfpCollectionScopeAsync(query, cancellationToken).ConfigureAwait(false);
            return generic ?? ApplyPriorSourceFallback(priorSources, query);
        }

        var metadataItems = await LoadMetadataItemsAsync(cancellationToken).ConfigureAwait(false);
        if (metadataItems is null)
        {
            return null;
        }

        var ranked = metadataItems
            .Select(item => new
            {
                Metadata = item,
                Score = ScoreFileAgainstTerms(item.Descriptor.FileName, item.Tags, terms)
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Metadata.UploadedAt)
            .ToList();

        if (ranked.Count == 0)
        {
            // RFP collection prompts whose terms do not appear in file names (e.g., "in the past 5 years")
            // still resolve to all RFP-ish registered files when the user asks for a list/summary.
            var generic = await ResolveGenericRfpCollectionScopeAsync(query, cancellationToken).ConfigureAwait(false);
            return generic ?? ApplyPriorSourceFallback(priorSources, query);
        }

        var topScore = ranked[0].Score;
        var runnerUpScore = ranked.Count > 1 ? ranked[1].Score : 0;

        // Single clear winner: the prompt names a distinguishing feature (e.g., a year) present in one file.
        if (topScore >= 2 && topScore > runnerUpScore)
        {
            return new PromptSourceScope(new[] { ToKnowledgeSource(ranked[0].Metadata) }, IsSingle: true);
        }

        // Multi-source collection request: several files match and the prompt asks for a list/summary.
        if (ranked.Count >= 2 && HasCollectionIntent(query))
        {
            return new PromptSourceScope(
                ranked.Take(10).Select(candidate => ToKnowledgeSource(candidate.Metadata)).ToList(),
                IsSingle: false);
        }

        return ApplyPriorSourceFallback(priorSources, query);
    }

    private async Task<PromptSourceScope?> ResolveGenericRfpCollectionScopeAsync(string query, CancellationToken cancellationToken)
    {
        if (!IsRfpPrompt(query) || !HasCollectionIntent(query))
        {
            return null;
        }

        var metadataItems = await LoadMetadataItemsAsync(cancellationToken).ConfigureAwait(false);
        if (metadataItems is null)
        {
            return null;
        }

        var rfpFiles = metadataItems
            .Where(item => IsMetadataCandidate(query, item))
            .OrderByDescending(item => item.UploadedAt)
            .ToList();
        return rfpFiles.Count == 0
            ? null
            : new PromptSourceScope(rfpFiles.Select(ToKnowledgeSource).ToList(), IsSingle: false);
    }

    private async Task<IReadOnlyList<FileMetadata>?> LoadMetadataItemsAsync(CancellationToken cancellationToken)
    {
        var result = await _metadataRepository!
            .SearchAsync(new SearchRequest(null, 1, 200), cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess && result.Value is not null ? result.Value.Items : null;
    }

    private static IReadOnlyList<string> SignificantQueryTerms(string value)
    {
        return QueryTerms(value)
            .Where(term => term.Length >= 3 && !PromptStopWords.Contains(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ScoreFileAgainstTerms(string fileName, IReadOnlyDictionary<string, string>? tags, IReadOnlyList<string> terms)
    {
        var score = 0;
        foreach (var term in terms)
        {
            if (fileName.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }
            else if (tags is not null && tags.Any(tag =>
                tag.Key.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                tag.Value.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                score += 1;
            }
        }

        return score;
    }

    private static bool HasCollectionIntent(string value)
    {
        return ContainsAny(value,
            "all", "each", "list", "registered", "summarize", "summary", "overview",
            "past", "last", "opportunities", "projects", "portfolio", "status", "tracking");
    }

    private sealed record SessionMemory(IReadOnlyList<KnowledgeSource> Sources, DateTimeOffset UpdatedAt);

    private static PromptSourceScope? ApplyPriorSourceFallback(IReadOnlyList<KnowledgeSource>? priorSources, string query)
    {
        return priorSources is { Count: > 0 } && HasFollowUpReference(query)
            ? new PromptSourceScope(priorSources, IsSingle: false)
            : null;
    }

    private static bool HasFollowUpReference(string value)
    {
        return HasCollectionIntent(value)
            || ContainsAny(value, "these", "those", "this", "that", "them", "they", "it",
                "previous", "prior", "above", "mentioned", "listed", "second", "next", "other");
    }

    private IReadOnlyList<KnowledgeSource>? GetPriorSources(ChatJobWorkItem item)
    {
        return item.Plan.SessionId is Guid sessionId && _sessionMemory.TryGetValue(sessionId, out var memory)
            ? memory.Sources
            : null;
    }

    private static ChatSession BuildChatSession(ChatJobWorkItem item)
    {
        var session = new ChatSession
        {
            Id = item.Plan.SessionId ?? Guid.NewGuid(),
            Title = "Copilot session"
        };
        if (item.Plan.HistoryMessages is { Count: > 0 })
        {
            session.Messages.AddRange(item.Plan.HistoryMessages);
        }

        return session;
    }

    private static IReadOnlyList<KnowledgeSource> ExtractRetrievalSources(IReadOnlyList<SearchResult> retrieval)
    {
        return retrieval
            .GroupBy(result => result.Chunk.SourceId)
            .Select(group => new KnowledgeSource(
                group.Key,
                group.First().Citations.FirstOrDefault() ?? group.Key.ToString(),
                DateTimeOffset.UtcNow))
            .ToList();
    }

    private static KnowledgeSource ToKnowledgeSource(FileMetadata metadata)
    {
        return new KnowledgeSource(metadata.Descriptor.FileId, metadata.Descriptor.FileName, metadata.UploadedAt);
    }

    private async Task<IReadOnlyList<Guid>?> ResolveThemeSourceIdsAsync(ChatJobWorkItem item, CancellationToken cancellationToken)
    {
        if (item.Plan.ThemeFilter is not { Count: > 0 } || _themeService is null)
        {
            return null;
        }

        var result = await _themeService
            .ResolveSourceIdsAsync(item.Plan.ThemeFilter, cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }

    private async Task<IReadOnlyList<SearchResult>?> TrySourceScopedRetrievalAsync(
        ChatJobWorkItem item,
        ChatJobState state,
        int topK,
        CancellationToken cancellationToken,
        PromptSourceScope? preResolvedScope = null)
    {
        var scope = preResolvedScope ?? await ResolvePromptSourceScopeAsync(item.Prompt, cancellationToken, GetPriorSources(item)).ConfigureAwait(false);
        if (scope is null)
        {
            return null;
        }

        var themeSourceIds = await ResolveThemeSourceIdsAsync(item, cancellationToken).ConfigureAwait(false);

        if (scope.IsSingle && scope.Sources.Count == 1)
        {
            var source = scope.Sources[0];
            if (themeSourceIds is not null && !themeSourceIds.Contains(source.SourceId))
            {
                await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
                {
                    Stage = "RAGS retrieval",
                    Message = $"Source {source.SourceName} is outside the session knowledge themes; no retrieval for this document."
                }, cancellationToken).ConfigureAwait(false);
                return Array.Empty<SearchResult>();
            }

            state.Update("RAGS retrieval", $"Source-scoped retrieval for {source.SourceName}.", force: true);
            await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
            {
                Stage = "RAGS retrieval",
                Message = $"Resolved single registered source {source.SourceName}; scoping vector search to that document."
            }, cancellationToken).ConfigureAwait(false);

            var attempt = await RetrieveWithQueryVariantsAsync(
                item.JobId,
                state,
                BuildMandatoryFallbackQueries(item.Prompt),
                topK,
                source.SourceId,
                cancellationToken).ConfigureAwait(false);
            return attempt.Results;
        }

        var themeScopedSources = themeSourceIds is null
            ? scope.Sources
            : scope.Sources.Where(source => themeSourceIds.Contains(source.SourceId)).ToList();
        if (themeScopedSources.Count == 0)
        {
            await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
            {
                Stage = "RAGS retrieval",
                Message = "None of the resolved sources are inside the session knowledge themes; no retrieval."
            }, cancellationToken).ConfigureAwait(false);
            return Array.Empty<SearchResult>();
        }

        await _progressStore.AppendMessageAsync(item.JobId, new ChatProgressMessage
        {
            Stage = "RAGS retrieval",
            Message = $"Prompt requests a collection view; retrieving independently for {themeScopedSources.Count} matching registered source(s)."
        }, cancellationToken).ConfigureAwait(false);

        return await RetrieveScopedCollectionResultsAsync(
            item.JobId,
            state,
            item.Prompt,
            topK,
            themeScopedSources,
            Array.Empty<SearchResult>(),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SearchResult>?> RunSourceAwareBroadAnalysisAsync(
        ChatJobWorkItem item,
        ChatJobState state,
        CancellationToken cancellationToken)
    {
        var scope = await ResolvePromptSourceScopeAsync(item.Prompt, cancellationToken, GetPriorSources(item)).ConfigureAwait(false);
        if (scope is null)
        {
            return await RunGlobalSearchAsync(item, state, cancellationToken).ConfigureAwait(false);
        }

        var topK = Math.Max(1, item.EstimatedRetrievalCount);
        return await TrySourceScopedRetrievalAsync(item, state, topK, cancellationToken, scope).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> BuildMandatoryFallbackQueries(string query)
    {
        var queries = new List<string> { query };
        if (IsRfpPrompt(query))
        {
            queries.Add("RFP request for proposal registered opportunities procurement");
            queries.Add("request for proposal");
            queries.Add("RFP");
        }

        if (IsDocumentRequirementPrompt(query))
        {
            queries.Add("required features requirements capabilities engagement");
            queries.Add("features requirements");
            queries.Add("requirements");
        }

        return queries
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<SearchResult> BuildRegisteredSourceMetadataResults(IReadOnlyList<KnowledgeSource> sources)
    {
        return sources
            .Take(10)
            .Select((source, index) =>
            {
                var content = $"Registered repository source: {source.SourceName}. " +
                    $"SourceId: {source.SourceId}. " +
                    $"Registered or uploaded at: {source.UploadedAt:O}. " +
                    "Use this metadata as internal evidence that the source exists in the Aletheia repository when RAGS chunks are not available.";
                return new SearchResult(
                    new Chunk(Guid.NewGuid(), source.SourceId, content, index),
                    0.5f,
                    new[] { source.SourceName },
                    retrievalStrategy: "metadata",
                    rank: index + 1);
            })
            .ToList();
    }

    private static bool IsRfpPrompt(string value)
    {
        return value.Contains("rfp", StringComparison.OrdinalIgnoreCase)
            || value.Contains("request for proposal", StringComparison.OrdinalIgnoreCase)
            || value.Contains("procurement", StringComparison.OrdinalIgnoreCase)
            || value.Contains("opportunit", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScopedCollectionPrompt(string value)
    {
        if (!IsRfpPrompt(value) && !IsDocumentRequirementPrompt(value))
        {
            return false;
        }

        return value.Contains("all", StringComparison.OrdinalIgnoreCase)
            || value.Contains("each", StringComparison.OrdinalIgnoreCase)
            || value.Contains("list", StringComparison.OrdinalIgnoreCase)
            || value.Contains("registered", StringComparison.OrdinalIgnoreCase)
            || value.Contains("summarize", StringComparison.OrdinalIgnoreCase)
            || value.Contains("summary", StringComparison.OrdinalIgnoreCase)
            || value.Contains("opportunities", StringComparison.OrdinalIgnoreCase)
            || value.Contains("past", StringComparison.OrdinalIgnoreCase)
            || value.Contains("last", StringComparison.OrdinalIgnoreCase)
            || value.Contains("feature", StringComparison.OrdinalIgnoreCase)
            || value.Contains("require", StringComparison.OrdinalIgnoreCase)
            || value.Contains("engagement", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMetadataCandidate(string query, FileMetadata metadata)
    {
        var fileName = metadata.Descriptor.FileName;
        if (IsRfpPrompt(query)
            && (IsRfpPrompt(fileName) || metadata.Tags.Any(tag => IsRfpPrompt(tag.Key) || IsRfpPrompt(tag.Value))))
        {
            return true;
        }

        if (!IsDocumentRequirementPrompt(query))
        {
            return false;
        }

        return QueryTerms(query)
            .Where(term => term.Length >= 3)
            .Any(term => fileName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || metadata.Tags.Any(tag =>
                    tag.Key.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || tag.Value.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsDocumentRequirementPrompt(string value)
    {
        return ContainsAny(value, "requirement", "requirements", "required", "feature", "features", "capability", "capabilities")
            && ContainsAny(value, "cmp", "document", "docx", "file", "artifact", "engagement");
    }

    private static IReadOnlyList<string> QueryTerms(string value)
    {
        return value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.Trim(',', '.', ':', ';', '"', '\'', '(', ')', '[', ']'))
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
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

    private sealed record ToolInvocationResult(
        IReadOnlyList<SearchResult> Results,
        int InvocationCount,
        string? Error = null,
        string? EffectiveToolName = null);

    private sealed record RetrievalAttempt(IReadOnlyList<SearchResult> Results, string? Error)
    {
        public int Count => Results.Count;
    }

    private ChatRequestOptions? BuildOptions(ChatJobWorkItem item, IReadOnlyList<SearchResult>? retrieval)
    {
        var options = item.Mode switch
        {
            ChatExecutionMode.StructuredSynthesis => new ChatRequestOptions { OutputFormat = "table" },
            _ => new ChatRequestOptions()
        };

        if (_templateRegistry is not null && retrieval is { Count: > 0 })
        {
            var sourceName = retrieval
                .SelectMany(result => result.Citations)
                .FirstOrDefault(citation => !string.IsNullOrWhiteSpace(citation));
            var sections = sourceName is null ? null : _templateRegistry.TryGetSections(sourceName);
            if (sections is { Count: > 0 })
            {
                options.SectionOutline = sections;
            }
        }

        if (retrieval is { Count: > 0 })
        {
            options.RetrievalResults = retrieval;
            options.UseProvidedRetrievalOnly = item.Plan.RequiresToolCall;
            options.ScopeInstruction = IsDocumentRequirementPrompt(item.Prompt)
                ? "The retrieved context below is document-level evidence for a scoped feature/requirement question. List the required features or requirements found in the represented source documents, cite each item, and do not mention graph communities, chunk counts, retrieval strategies, or index internals."
                : IsScopedCollectionPrompt(item.Prompt)
                    ? "The retrieved context below is the lazy scoped WRAGS coverage for the user's requested category. Treat each distinct SourceId as an in-scope registered document. For list or summary-list requests, include every represented source and provide a brief cited summary for each."
                    : "Use the verified repository tool context below as the authoritative WRAGS/RAGS context for this answer.";
        }

        return options;
    }

    private async Task<Result<T>> RunWithHeartbeatAsync<T>(
        Guid jobId,
        ChatJobState state,
        string stage,
        string detail,
        Func<CancellationToken, Task<Result<T>>> operation,
        CancellationToken cancellationToken)
    {
        await EmitExecutionHeartbeatAsync(jobId, state, stage, detail, cancellationToken).ConfigureAwait(false);
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

                await EmitExecutionHeartbeatAsync(jobId, state, stage, detail, heartbeatCts.Token).ConfigureAwait(false);
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

    private async Task EmitExecutionHeartbeatAsync(
        Guid jobId,
        ChatJobState state,
        string stage,
        string detail,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.Update(stage, detail, force: true);
        state.RecordHeartbeat();

        try
        {
            await _progressStore.AppendHeartbeatAsync(jobId, new ChatProgressHeartbeat
            {
                Stage = stage,
                Detail = detail,
                PercentComplete = state.PercentComplete,
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to append heartbeat for job {JobId} at stage {Stage}.", jobId, stage);
        }
    }

    private TimeSpan SelectHeartbeatInterval(string stage)
    {
        return stage is "Synthesis" or "Global search" or "RAGS retrieval" or "Tool call"
            ? TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds)
            : TimeSpan.FromSeconds(_options.LongWaitHeartbeatIntervalSeconds);
    }

    private TimeSpan SelectWatchdogThreshold()
    {
        var missedHeartbeatThreshold = TimeSpan.FromSeconds(
            Math.Max(1, _options.HeartbeatIntervalSeconds)
            * Math.Max(1, _options.HeartbeatWatchdogMissedThreshold));
        var longWaitThreshold = TimeSpan.FromSeconds(
            Math.Max(1, _options.LongWaitHeartbeatIntervalSeconds)
            + Math.Max(1, _options.HeartbeatIntervalSeconds));
        return missedHeartbeatThreshold > longWaitThreshold ? missedHeartbeatThreshold : longWaitThreshold;
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

    private async Task MarkStepFailedAsync(Guid jobId, string name, string error, CancellationToken cancellationToken)
    {
        var progress = await _progressStore.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (!progress.IsSuccess || progress.Value is null)
        {
            return;
        }

        var step = progress.Value.Steps.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        await _progressStore.UpdateStepAsync(jobId, new ChatProgressStep
        {
            Name = name,
            Status = ChatProgressStepStatus.Failed,
            Order = step?.Order ?? ProgressStages.IndexOf(name),
            StartedAt = step?.StartedAt,
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
