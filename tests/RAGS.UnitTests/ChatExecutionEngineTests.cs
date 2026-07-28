using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.Planning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RAGS.UnitTests;

public class ChatExecutionEngineTests
{
    [Fact]
    public async Task StartAsync_rejects_unapproved_plan()
    {
        var services = CreateServices();
        var plan = await services.Approval.CreatePlanAsync("summarize corpus");
        Assert.True(plan.IsSuccess);

        var result = await services.Execution.StartAsync(plan.Value!.PlanId);

        Assert.True(result.IsFailure);
        Assert.Contains("approved", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartAsync_returns_snapshot_for_approved_plan()
    {
        var services = CreateServices();
        var plan = await services.Approval.CreatePlanAsync("summarize corpus");
        Assert.True(plan.IsSuccess);
        var approved = await services.Approval.ApproveAsync(plan.Value!.PlanId);
        Assert.True(approved.IsSuccess);

        var result = await services.Execution.StartAsync(plan.Value.PlanId);

        Assert.True(result.IsSuccess);
        Assert.Equal(ChatJobStatus.Queued, result.Value!.Status);
        Assert.Equal(plan.Value.PlanId, result.Value.PlanId);
    }

    [Fact]
    public async Task StartAsync_rejects_missing_plan()
    {
        var services = CreateServices();

        var result = await services.Execution.StartAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStatusAsync_returns_snapshot_after_start()
    {
        var services = CreateServices();
        var plan = await services.Approval.CreatePlanAsync("summarize corpus");
        Assert.True(plan.IsSuccess);
        var approved = await services.Approval.ApproveAsync(plan.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        var result = await services.Execution.GetStatusAsync(started.Value!.JobId);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(started.Value.JobId, result.Value!.JobId);
    }

    [Fact]
    public async Task CancelAsync_marks_running_job_cancelled()
    {
        var services = CreateServices(startWorkerLoop: false);
        var plan = await services.Approval.CreatePlanAsync("summarize corpus");
        Assert.True(plan.IsSuccess);
        var approved = await services.Approval.ApproveAsync(plan.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        var cancelResult = await services.Execution.CancelAsync(started.Value!.JobId);
        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);

        Assert.True(cancelResult.IsSuccess);
        Assert.True(status.IsSuccess);
        Assert.Equal(ChatJobStatus.Cancelled, status.Value!.Status);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Cancelled, progress.Value!.Status);
        Assert.Equal("Cancelled by user.", progress.Value.Error);
    }

    [Fact]
    public async Task CancelAsync_fails_when_job_not_found()
    {
        var services = CreateServices();

        var result = await services.Execution.CancelAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Engine_executes_retrieval_and_synthesis_to_completion()
    {
        var services = CreateServices();
        var plan = await services.Approval.CreatePlanAsync("what does CMP say?");
        Assert.True(plan.IsSuccess);
        var approved = await services.Approval.ApproveAsync(plan.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var result = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Status is ChatJobStatus.Succeeded or ChatJobStatus.Failed or ChatJobStatus.Cancelled);
        Assert.True(progress.IsSuccess);
        Assert.NotNull(progress.Value);
        Assert.True(progress.Value!.Steps.Count > 0);
    }



    [Fact]
    public async Task Engine_records_telemetry_on_success()
    {
        var services = CreateServices();
        var plan = await services.Approval.CreatePlanAsync("what does CMP say?");
        Assert.True(plan.IsSuccess);
        var approved = await services.Approval.ApproveAsync(plan.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(progress.IsSuccess);
        Assert.NotNull(progress.Value);
        Assert.NotNull(progress.Value!.Telemetry);
        Assert.True(progress.Value.Telemetry.ElapsedSeconds >= 0);
        Assert.True(progress.Value.Telemetry.UsedProviderMetrics);
        Assert.NotEmpty(progress.Value.Telemetry.EstimateComparisonSummary);
    }

    [Fact]
    public async Task Final_result_includes_telemetry_summary()
    {
        var services = CreateServices();
        var plan = await services.Approval.CreatePlanAsync("what does CMP say?");
        Assert.True(plan.IsSuccess);
        var approved = await services.Approval.ApproveAsync(plan.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.NotNull(status.Value);
        Assert.NotNull(status.Value!.Result);
        Assert.Contains("Execution telemetry", status.Value.Result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Engine_marks_job_failed_when_synthesis_fails()
    {
        var services = CreateServices(() => new FailingCopilotService());
        var plan = await services.Approval.CreatePlanAsync("what does CMP say?");
        Assert.True(plan.IsSuccess);
        var approved = await services.Approval.ApproveAsync(plan.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.Equal(ChatJobStatus.Failed, status.Value!.Status);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Failed, progress.Value!.Status);
        Assert.NotNull(progress.Value.Error);
    }

    [Fact]
    public void CopilotController_has_chat_job_endpoints()
    {
        var controller = typeof(Repository.API.Controllers.CopilotController);
        var methods = controller.GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(HttpPostAttribute), false).Any()
                || m.GetCustomAttributes(typeof(HttpGetAttribute), false).Any())
            .Select(m => new
            {
                m.Name,
                Route = (m.GetCustomAttributes(typeof(HttpPostAttribute), false).FirstOrDefault() as HttpPostAttribute)?.Template
                    ?? (m.GetCustomAttributes(typeof(HttpGetAttribute), false).FirstOrDefault() as HttpGetAttribute)?.Template
            })
            .ToList();

        Assert.Contains(methods, m => m.Name == "ExecutePlan" && m.Route == "plans/{planId:guid}/execute");
        Assert.Contains(methods, m => m.Name == "GetChatJob" && m.Route == "jobs/chat/{jobId:guid}");
        Assert.Contains(methods, m => m.Name == "CancelChatJob" && m.Route == "jobs/chat/{jobId:guid}/cancel");
        Assert.Contains(methods, m => m.Name == "ListChatJobs" && m.Route == "jobs/chat");
        Assert.Contains(methods, m => m.Name == "GetPlanProgress" && m.Route == "plans/{planId:guid}/progress");
        Assert.Contains(methods, m => m.Name == "GetTelemetry" && m.Route == "jobs/chat/{jobId:guid}/telemetry");
    }

    [Fact]
    public async Task GetProgressAsync_returns_steps_and_heartbeats()
    {
        var services = CreateServices();
        var plan = await services.Approval.CreatePlanAsync("what does CMP say?");
        Assert.True(plan.IsSuccess);
        var approved = await services.Approval.ApproveAsync(plan.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(progress.IsSuccess);
        Assert.NotNull(progress.Value);
        Assert.True(progress.Value!.Steps.Count > 0);
    }

    [Fact]
    public async Task GetProgressAsync_returns_partial_result()
    {
        var services = CreateServices();
        var plan = await services.Approval.CreatePlanAsync("what does CMP say?");
        Assert.True(plan.IsSuccess);
        var approved = await services.Approval.ApproveAsync(plan.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(progress.IsSuccess);
        Assert.NotNull(progress.Value);
        Assert.NotNull(progress.Value!.PartialResult);
        Assert.Contains("Retrieved", progress.Value.PartialResult);
    }

    [Fact]
    public async Task Engine_honors_step_timeouts()
    {
        var options = new ChatExecutionEngineOptions
        {
            DefaultStepTimeoutSeconds = 1,
            OverallJobTimeoutSeconds = 10,
            HeartbeatIntervalSeconds = 1,
            LongWaitHeartbeatIntervalSeconds = 2,
            HeartbeatWatchdogMissedThreshold = 5,
            SmallCorpusDocumentThreshold = 5,
            SmallCorpusTimeoutSeconds = 1
        };
        var services = CreateServices(
            rags: new HangingRagsService(),
            graphRag: new HangingGraphRagService(),
            lazyGraphRag: new HangingLazyGraphRagService(),
            options: options);
        var plan = await services.Approval.CreatePlanAsync("summarize corpus");
        Assert.True(plan.IsSuccess);
        var approved = await services.Approval.ApproveAsync(plan.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(10));

        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(progress.IsSuccess);
        Assert.NotNull(progress.Value);
        Assert.Equal(ChatJobStatus.Failed, progress.Value!.Status);
        var retrievalStep = progress.Value.Steps.FirstOrDefault(s => s.Name == "Retrieving context");
        Assert.NotNull(retrievalStep);
        Assert.Equal(ChatProgressStepStatus.Failed, retrievalStep.Status);
    }

    [Fact]
    public async Task Engine_transitions_to_failed_on_exception()
    {
        var services = CreateServices(() => new ThrowingCopilotService());
        var plan = await services.Approval.CreatePlanAsync("what does CMP say?");
        Assert.True(plan.IsSuccess);
        var approved = await services.Approval.ApproveAsync(plan.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.Equal(ChatJobStatus.Failed, status.Value!.Status);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Failed, progress.Value!.Status);
        Assert.True(progress.Value.Steps.All(s => s.Status != ChatProgressStepStatus.Running));
        Assert.NotNull(progress.Value.Error);
    }

    [Fact]
    public async Task Engine_marks_all_successful_steps_completed()
    {
        var services = CreateServices();
        var plan = await services.Approval.CreatePlanAsync("what does CMP say?");
        Assert.True(plan.IsSuccess);
        var approved = await services.Approval.ApproveAsync(plan.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.True(progress.IsSuccess);
        Assert.Null(progress.Value!.Error);
        Assert.Equal(ChatJobStatus.Succeeded, status.Value!.Status);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value.Status);
        Assert.DoesNotContain(progress.Value.Steps, s => s.Status == ChatProgressStepStatus.Failed);
        Assert.DoesNotContain(progress.Value.Steps, s => s.Status == ChatProgressStepStatus.Running);
        Assert.Contains(progress.Value.Steps, s => s.Name == "Completed" && s.Status == ChatProgressStepStatus.Completed);
        Assert.Contains(progress.Value.Steps, s => s.Name == "Planning" && s.Status == ChatProgressStepStatus.Completed);
    }

    [Fact]
    public async Task Engine_completes_instantly_on_small_corpus()
    {
        var options = new ChatExecutionEngineOptions
        {
            DefaultStepTimeoutSeconds = 30,
            OverallJobTimeoutSeconds = 60,
            HeartbeatIntervalSeconds = 1,
            LongWaitHeartbeatIntervalSeconds = 2,
            HeartbeatWatchdogMissedThreshold = 5,
            SmallCorpusDocumentThreshold = 5,
            SmallCorpusTimeoutSeconds = 5
        };
        var services = CreateServices(
            graphRag: new HangingGraphRagService(),
            lazyGraphRag: new HangingLazyGraphRagService(),
            options: options);
        var plan = await services.Approval.CreatePlanAsync("what does CMP say?");
        Assert.True(plan.IsSuccess);
        var approved = await services.Approval.ApproveAsync(plan.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(10));
        sw.Stop();

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, status.Value!.Status);
        Assert.True(sw.ElapsedMilliseconds < 10000, $"Small corpus execution took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Engine_rejects_expired_plan()
    {
        var services = CreateServices();
        var plan = await services.Approval.CreatePlanAsync("summarize corpus");
        Assert.True(plan.IsSuccess);
        var approved = await services.Approval.ApproveAsync(plan.Value!.PlanId);
        Assert.True(approved.IsSuccess);

        var repositoryField = services.Approval.GetType().GetField("_repository", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(repositoryField);
        var repository = repositoryField.GetValue(services.Approval);
        Assert.NotNull(repository);
        var getMethod = repository!.GetType().GetMethod("GetAsync", new[] { typeof(Guid), typeof(CancellationToken) });
        Assert.NotNull(getMethod);
        var getTask = (Task<Result<ChatPlanRecord?>>?)getMethod!.Invoke(repository, new object[] { plan.Value!.PlanId, CancellationToken.None });
        Assert.NotNull(getTask);
        var getResult = await getTask;
        Assert.True(getResult.IsSuccess);
        Assert.NotNull(getResult.Value);
        var existing = getResult.Value!;
        var expiredRecord = new ChatPlanRecord
        {
            PlanId = existing.PlanId,
            Prompt = existing.Prompt,
            Mode = existing.Mode,
            Status = existing.Status,
            Steps = existing.Steps,
            EstimatedSecondsMin = existing.EstimatedSecondsMin,
            EstimatedSecondsMax = existing.EstimatedSecondsMax,
            EstimatedLlmCalls = existing.EstimatedLlmCalls,
            EstimatedInputTokens = existing.EstimatedInputTokens,
            EstimatedOutputTokens = existing.EstimatedOutputTokens,
            EstimatedRetrievalCount = existing.EstimatedRetrievalCount,
            RequiresApproval = existing.RequiresApproval,
            RequiresToolCall = existing.RequiresToolCall,
            ToolName = existing.ToolName,
            ToolArguments = existing.ToolArguments,
            ReviewedBy = existing.ReviewedBy,
            ReviewedAt = existing.ReviewedAt,
            CreatedAt = existing.CreatedAt,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CancellationReason = existing.CancellationReason
        };
        var saveMethod = repository.GetType().GetMethod("SaveAsync", new[] { typeof(ChatPlanRecord), typeof(CancellationToken) });
        Assert.NotNull(saveMethod);
        var saveTask = (Task<Result>?)saveMethod!.Invoke(repository, new object[] { expiredRecord, CancellationToken.None });
        Assert.NotNull(saveTask);
        var saveResult = await saveTask;
        Assert.True(saveResult.IsSuccess);

        var result = await services.Execution.StartAsync(plan.Value.PlanId);

        Assert.True(result.IsFailure);
        Assert.Contains("expired", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Engine_invokes_repository_tool_for_rfp_query()
    {
        var sourceId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var toolRags = new ToolCapturingRagsService(new[]
        {
            new SearchResult(new Chunk(chunkId, sourceId, "RFP 2022 summary content", 0), 0.92f, new[] { "RFP-2022.docx" }, retrievalStrategy: "rags")
        });
        var services = CreateServices(rags: toolRags);
        var plan = await services.Approval.CreatePlanAsync("Summarize all RFPs in the repository");
        Assert.True(plan.IsSuccess);
        Assert.True(plan.Value!.RequiresToolCall);
        Assert.Contains("AletheiaKnowledgePlugin", plan.Value.ToolName);
        var approved = await services.Approval.ApproveAsync(plan.Value.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(progress.IsSuccess);
        Assert.NotNull(progress.Value);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value!.Status);
        Assert.NotNull(toolRags.LastRequest);
        Assert.Contains(toolRags.LastRequest!.Query, "all RFPs", StringComparison.OrdinalIgnoreCase);
        var telemetry = progress.Value.Telemetry;
        Assert.NotNull(telemetry);
        Assert.NotEmpty(telemetry.ToolName);
        Assert.True(telemetry.ToolInvocationCount > 0);
        Assert.Contains(progress.Value.Steps, s => s.Name == "Retrieving context" && s.Status == ChatProgressStepStatus.Completed);
        Assert.Contains(progress.Value.Steps, s => s.Name.StartsWith("Call repository tool", StringComparison.OrdinalIgnoreCase) && s.Status == ChatProgressStepStatus.Completed);
    }

    public static TestServices CreateServices(
        Func<ICopilotService>? copilotFactory = null,
        IRagsService? rags = null,
        IGraphRagService? graphRag = null,
        ILazyGraphRagService? lazyGraphRag = null,
        ChatExecutionEngineOptions? options = null,
        bool startWorkerLoop = true)
    {
        var planning = new ChatPlanningService();
        var repository = new InMemoryChatPlanRepository();
        var approval = new ChatPlanApprovalService(planning, repository);
        var logger = new NoopLogger<ChatExecutionEngine>();
        var copilot = copilotFactory?.Invoke() ?? new FakeCopilotService();
        var ragsService = rags ?? new FakeRagsService(new[]
        {
            new SearchResult(new Chunk(Guid.NewGuid(), Guid.NewGuid(), "sample", 0), 0.9f)
        });
        var graphRagService = graphRag ?? new FakeGraphRagService();
        var lazyGraphRagService = lazyGraphRag ?? new FakeLazyGraphRagService();
        var globalGraphSearchService = new FakeGlobalGraphSearchService();
        var knowledgePlugin = new Aletheia.RAGS.Application.SemanticKernel.AletheiaKnowledgePlugin(
            ragsService,
            graphRagService,
            lazyGraphRagService,
            globalGraphSearchService);
        var progressStore = new InMemoryChatProgressStore();
        var telemetryService = new ChatTelemetryService();
        var optionsValue = options ?? new ChatExecutionEngineOptions
        {
            DefaultStepTimeoutSeconds = 30,
            OverallJobTimeoutSeconds = 60,
            HeartbeatIntervalSeconds = 1,
            LongWaitHeartbeatIntervalSeconds = 2,
            HeartbeatWatchdogMissedThreshold = 5,
            SmallCorpusDocumentThreshold = 5,
            SmallCorpusTimeoutSeconds = 5
        };
        var execution = new ChatExecutionEngine(approval, copilot, ragsService, graphRagService, lazyGraphRagService, globalGraphSearchService, progressStore, telemetryService, Options.Create(optionsValue), logger);
        var host = new FakeHost(execution);
        if (startWorkerLoop)
        {
            _ = execution.StartAsync(CancellationToken.None);
        }

        return new TestServices(approval, execution, host, optionsValue);
    }

    public sealed class TestServices : IDisposable
    {
        public TestServices(
            IChatPlanApprovalService approval,
            IChatExecutionService execution,
            FakeHost host,
            ChatExecutionEngineOptions options)
        {
            Approval = approval;
            Execution = execution;
            Host = host;
            Options = options;
        }

        public IChatPlanApprovalService Approval { get; }
        public IChatExecutionService Execution { get; }
        public FakeHost Host { get; }
        public ChatExecutionEngineOptions Options { get; }

        public async Task RunUntilTerminalAsync(Guid jobId, TimeSpan timeout)
        {
            var started = DateTimeOffset.UtcNow;
            while (DateTimeOffset.UtcNow - started < timeout)
            {
                var status = await Execution.GetStatusAsync(jobId).ConfigureAwait(false);
                if (status.Value?.Status is ChatJobStatus.Succeeded or ChatJobStatus.Failed or ChatJobStatus.Cancelled)
                {
                    return;
                }

                await Task.Delay(50).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            Host.Dispose();
        }
    }

    public sealed class FakeHost : IDisposable
    {
        private readonly ChatExecutionEngine _engine;

        public FakeHost(ChatExecutionEngine engine)
        {
            _engine = engine;
        }

        public void Dispose()
        {
        }
    }

    private sealed class NoopLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class FakeCopilotService : ICopilotService
    {
        public Task<Result<ChatMessage>> ChatAsync(
            ChatSession session,
            string userMessage,
            ChatRequestOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ChatMessage>.Success(new ChatMessage
            {
                Role = "assistant",
                Content = $"Answer to: {userMessage}",
                Stats = new ChatCompletionStats
                {
                    RetrievedContextCount = 1,
                    CitationCount = 0
                }
            }));
        }

        public Task<Result<SummaryResponse>> SummarizeAsync(SummaryRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<SummaryResponse>.Success(new SummaryResponse()));
        }

        public Task<Result<ExplanationResponse>> ExplainAsync(ExplanationRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ExplanationResponse>.Success(new ExplanationResponse()));
        }

        public Task<Result<DiscoveryResponse>> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<DiscoveryResponse>.Success(new DiscoveryResponse()));
        }
    }

    private sealed class FakeRagsService : IRagsService
    {
        private readonly IReadOnlyList<SearchResult> _results;

        public FakeRagsService(IReadOnlyList<SearchResult> results)
        {
            _results = results;
        }

        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(_results));
        }
    }

    private sealed class FakeGraphRagService : IGraphRagService
    {
        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(
            string query,
            int topK = 5,
            int maxExpanded = 10,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
        }

        public Task<Result<GlobalSearchResult>> GlobalSearchAsync(string query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<GlobalSearchResult>.Success(new GlobalSearchResult("GraphRAG global answer.", Array.Empty<string>(), Array.Empty<SearchResult>())));
        }
    }

    private sealed class FakeLazyGraphRagService : ILazyGraphRagService
    {
        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(
            string query,
            int topK = 5,
            int maxExpanded = 10,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
        }

        public Task<Result<GlobalSearchResult>> GlobalSearchAsync(string query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<GlobalSearchResult>.Success(new GlobalSearchResult("LazyGraphRAG global answer.", Array.Empty<string>(), Array.Empty<SearchResult>())));
        }
    }

    private sealed class FailingCopilotService : ICopilotService
    {
        public Task<Result<ChatMessage>> ChatAsync(
            ChatSession session,
            string userMessage,
            ChatRequestOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ChatMessage>.Failure("LLM service unavailable."));
        }

        public Task<Result<SummaryResponse>> SummarizeAsync(SummaryRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<SummaryResponse>.Success(new SummaryResponse()));
        }

        public Task<Result<ExplanationResponse>> ExplainAsync(ExplanationRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ExplanationResponse>.Success(new ExplanationResponse()));
        }

        public Task<Result<DiscoveryResponse>> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<DiscoveryResponse>.Success(new DiscoveryResponse()));
        }
    }

    private sealed class ThrowingCopilotService : ICopilotService
    {
        public Task<Result<ChatMessage>> ChatAsync(
            ChatSession session,
            string userMessage,
            ChatRequestOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Synthesis threw an unhandled exception.");
        }

        public Task<Result<SummaryResponse>> SummarizeAsync(SummaryRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<SummaryResponse>.Success(new SummaryResponse()));
        }

        public Task<Result<ExplanationResponse>> ExplainAsync(ExplanationRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ExplanationResponse>.Success(new ExplanationResponse()));
        }

        public Task<Result<DiscoveryResponse>> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<DiscoveryResponse>.Success(new DiscoveryResponse()));
        }
    }

    private sealed class HangingRagsService : IRagsService
    {
        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public async Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>());
        }
    }

    private sealed class HangingGraphRagService : IGraphRagService
    {
        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(string query, int topK = 5, int maxExpanded = 10, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
        }

        public async Task<Result<GlobalSearchResult>> GlobalSearchAsync(string query, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return Result<GlobalSearchResult>.Success(new GlobalSearchResult("GraphRAG global answer.", Array.Empty<string>(), Array.Empty<SearchResult>()));
        }
    }

    private sealed class HangingLazyGraphRagService : ILazyGraphRagService
    {
        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(string query, int topK = 5, int maxExpanded = 10, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
        }

        public async Task<Result<GlobalSearchResult>> GlobalSearchAsync(string query, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return Result<GlobalSearchResult>.Success(new GlobalSearchResult("LazyGraphRAG global answer.", Array.Empty<string>(), Array.Empty<SearchResult>()));
        }
    }

    private sealed class FakeGlobalGraphSearchService : IGlobalGraphSearchService
    {
        public Task<Result<GlobalSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<GlobalSearchResult>.Success(new GlobalSearchResult("Global graph answer.", Array.Empty<string>(), Array.Empty<SearchResult>())));
        }
    }

    private sealed class ToolCapturingRagsService : IRagsService
    {
        public RetrievalRequest? LastRequest { get; private set; }

        private readonly IReadOnlyList<SearchResult> _results;

        public ToolCapturingRagsService(IReadOnlyList<SearchResult> results)
        {
            _results = results;
        }

        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(_results));
        }
    }
}
