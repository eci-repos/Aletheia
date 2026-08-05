using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application;
using Aletheia.RAGS.Application.Planning;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
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
    public async Task Engine_marks_job_failed_when_mandatory_tool_fails()
    {
        var rags = new FailingRagsService("vector store unavailable");
        var services = CreateServices(rags: rags);
        var planResult = await services.Approval.CreatePlanAsync("what does the CMP RFP require?");
        Assert.True(planResult.IsSuccess);
        var plan = planResult.Value!;
        Assert.True(plan.RequiresToolCall);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", plan.ToolName);
        var approved = await services.Approval.ApproveAsync(plan.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.Equal(ChatJobStatus.Failed, status.Value!.Status);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Failed, progress.Value!.Status);
        Assert.Contains("Mandatory repository tool failed", progress.Value.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Engine_marks_job_failed_when_mandatory_tool_returns_no_context()
    {
        var rags = new FakeRagsService(Array.Empty<SearchResult>());
        var services = CreateServices(rags: rags);
        var planResult = await services.Approval.CreatePlanAsync("what does the CMP RFP require?");
        Assert.True(planResult.IsSuccess);
        var plan = planResult.Value!;
        Assert.True(plan.RequiresToolCall);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", plan.ToolName);
        var approved = await services.Approval.ApproveAsync(plan.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.Equal(ChatJobStatus.Failed, status.Value!.Status);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Failed, progress.Value!.Status);
        Assert.Contains("Mandatory repository tool failed", progress.Value.Error, StringComparison.OrdinalIgnoreCase);
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
            MandatoryToolTimeoutSeconds = 1,
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
        var plan = await services.Approval.CreatePlanAsync("what does the CMP RFP require?");
        Assert.True(plan.IsSuccess);
        Assert.True(plan.Value!.RequiresToolCall);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", plan.Value.ToolName);
        var approved = await services.Approval.ApproveAsync(plan.Value.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(15));

        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(progress.IsSuccess);
        Assert.NotNull(progress.Value);
        Assert.Equal(ChatJobStatus.Failed, progress.Value!.Status);
        var toolStep = progress.Value.Steps.FirstOrDefault(s => s.Name == "Call repository tool");
        Assert.NotNull(toolStep);
        Assert.Equal(ChatProgressStepStatus.Failed, toolStep.Status);
        Assert.Contains("timed out", progress.Value.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("watchdog", progress.Value.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Engine_emits_heartbeats_during_mandatory_rags_tool_call()
    {
        var options = new ChatExecutionEngineOptions
        {
            DefaultStepTimeoutSeconds = 1,
            MandatoryToolTimeoutSeconds = 5,
            OverallJobTimeoutSeconds = 10,
            HeartbeatIntervalSeconds = 1,
            LongWaitHeartbeatIntervalSeconds = 1,
            HeartbeatWatchdogMissedThreshold = 5,
            SmallCorpusDocumentThreshold = 5,
            SmallCorpusTimeoutSeconds = 5
        };
        var results = new[]
        {
            new SearchResult(new Chunk(Guid.NewGuid(), Guid.NewGuid(), "CMP 2026 requires AI workflow automation.", 0), 0.93f)
        };
        var services = CreateServices(
            rags: new DelayedRagsService(TimeSpan.FromMilliseconds(1300), results),
            options: options);
        var plan = await services.Approval.CreatePlanAsync("Based on CMP 2026 list required features for this engagement");
        Assert.True(plan.IsSuccess);
        Assert.True(plan.Value!.RequiresToolCall);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", plan.Value.ToolName);
        var approved = await services.Approval.ApproveAsync(plan.Value.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(8));

        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        Assert.True(progress.IsSuccess);
        Assert.True(status.IsSuccess);
        Assert.NotNull(progress.Value);
        Assert.Equal(ChatJobStatus.Succeeded, status.Value!.Status);
        Assert.Contains(progress.Value!.Heartbeats, heartbeat =>
            heartbeat.Stage == "Tool call"
            && heartbeat.Detail.Contains("mandatory repository tool", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Engine_keeps_mandatory_tool_alive_when_long_wait_interval_exceeds_watchdog_threshold()
    {
        var options = new ChatExecutionEngineOptions
        {
            DefaultStepTimeoutSeconds = 1,
            MandatoryToolTimeoutSeconds = 8,
            OverallJobTimeoutSeconds = 10,
            HeartbeatIntervalSeconds = 1,
            LongWaitHeartbeatIntervalSeconds = 5,
            HeartbeatWatchdogMissedThreshold = 2,
            SmallCorpusDocumentThreshold = 5,
            SmallCorpusTimeoutSeconds = 5
        };
        var results = new[]
        {
            new SearchResult(new Chunk(Guid.NewGuid(), Guid.NewGuid(), "AI engagement opportunity includes managed analytics and automation support.", 0), 0.91f)
        };
        var services = CreateServices(
            rags: new DelayedRagsService(TimeSpan.FromMilliseconds(3500), results),
            options: options);
        var plan = await services.Approval.CreatePlanAsync("What opportunities are found for AI based engagements?");
        Assert.True(plan.IsSuccess);
        Assert.True(plan.Value!.RequiresToolCall);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", plan.Value.ToolName);
        var approved = await services.Approval.ApproveAsync(plan.Value.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(8));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, status.Value!.Status);
        Assert.DoesNotContain("watchdog", progress.Value?.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.True(progress.Value!.Heartbeats.Count(heartbeat => heartbeat.Stage == "Tool call") >= 2);
    }

    [Fact]
    public async Task Engine_runs_second_chat_job_when_first_mandatory_tool_is_still_running()
    {
        var options = new ChatExecutionEngineOptions
        {
            DefaultStepTimeoutSeconds = 1,
            MandatoryToolTimeoutSeconds = 30,
            OverallJobTimeoutSeconds = 60,
            HeartbeatIntervalSeconds = 1,
            LongWaitHeartbeatIntervalSeconds = 5,
            HeartbeatWatchdogMissedThreshold = 20,
            MaxConcurrentChatJobs = 2,
            SmallCorpusDocumentThreshold = 5,
            SmallCorpusTimeoutSeconds = 5
        };
        var sourceId = Guid.NewGuid();
        var rags = new QueryAwareRagsService(query => query.Contains("slow", StringComparison.OrdinalIgnoreCase)
            ? null
            : new[]
            {
                new SearchResult(new Chunk(Guid.NewGuid(), sourceId, "AI engagement opportunity includes analytics support.", 0), 0.92f)
            });
        var services = CreateServices(rags: rags, options: options);

        var slowPlan = await services.Approval.CreatePlanAsync("slow RFP opportunity request");
        Assert.True(slowPlan.IsSuccess);
        Assert.True(slowPlan.Value!.RequiresToolCall);
        var approvedSlow = await services.Approval.ApproveAsync(slowPlan.Value.PlanId);
        Assert.True(approvedSlow.IsSuccess);
        var slowJob = await services.Execution.StartAsync(slowPlan.Value.PlanId);
        Assert.True(slowJob.IsSuccess);

        var secondPlan = await services.Approval.CreatePlanAsync("What opportunities are found for AI based engagements?");
        Assert.True(secondPlan.IsSuccess);
        Assert.True(secondPlan.Value!.RequiresToolCall);
        var approvedSecond = await services.Approval.ApproveAsync(secondPlan.Value.PlanId);
        Assert.True(approvedSecond.IsSuccess);
        var secondJob = await services.Execution.StartAsync(secondPlan.Value.PlanId);
        Assert.True(secondJob.IsSuccess);

        await services.RunUntilTerminalAsync(secondJob.Value!.JobId, TimeSpan.FromSeconds(8));

        var secondStatus = await services.Execution.GetStatusAsync(secondJob.Value.JobId);
        var slowStatus = await services.Execution.GetStatusAsync(slowJob.Value!.JobId);
        Assert.True(secondStatus.IsSuccess);
        Assert.True(slowStatus.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, secondStatus.Value!.Status);
        Assert.Equal(ChatJobStatus.Running, slowStatus.Value!.Status);

        var cancelled = await services.Execution.CancelAsync(slowJob.Value.JobId);
        Assert.True(cancelled.IsSuccess);
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
            // Ensure audit step (Synthesis) is present but not failed
            Assert.Contains(progress.Value.Steps, s => s.Name == "Synthesis" && s.Status == ChatProgressStepStatus.Completed);
            Assert.DoesNotContain(progress.Value.Steps, s => s.Status == ChatProgressStepStatus.Failed);
            Assert.DoesNotContain(progress.Value.Steps, s => s.Status == ChatProgressStepStatus.Running);
            Assert.Contains(progress.Value.Steps, s => s.Name == "Completed" && s.Status == ChatProgressStepStatus.Completed);
            Assert.Contains(progress.Value.Steps, s => s.Name == "Planning" && s.Status == ChatProgressStepStatus.Completed);
    }

    [Fact]
    public async Task Engine_executes_rfp_ten_year_scenario_with_mandatory_tool_and_grounding_telemetry()
    {
        var services = CreateServices(() => new RfpScenarioCopilotService("semantic"));
        var planResult = await services.Approval.CreatePlanAsync("Summarize registered RFP opportunities in the past 10 years");
        Assert.True(planResult.IsSuccess);
        var plan = planResult.Value!;
        Assert.True(plan.RequiresToolCall);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", plan.ToolName);
        Assert.Contains(plan.Steps, step => step.StartsWith("Call repository tool", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Steps, step => step.Contains("Verify tool returned internal context before synthesis", StringComparison.OrdinalIgnoreCase));
        var approved = await services.Approval.ApproveAsync(plan.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, status.Value!.Status);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value!.Status);
        Assert.DoesNotContain(progress.Value.Steps, step => step.Status == ChatProgressStepStatus.Failed);
        Assert.Contains(progress.Value.Steps, step => step.Name == "Call repository tool" && step.Status == ChatProgressStepStatus.Completed);
        Assert.Contains(progress.Value.Steps, step => step.Name == "Verify tool returned internal context before synthesis" && step.Status == ChatProgressStepStatus.Completed);
        Assert.NotNull(progress.Value.Telemetry);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", progress.Value.Telemetry!.ToolName);
        Assert.Equal(1, progress.Value.Telemetry.ToolInvocationCount);
        Assert.Equal(2, progress.Value.Telemetry.CitationCount);
        Assert.Equal("semantic", progress.Value.Telemetry.RetrievalStrategy);
        Assert.Contains("Registered RFP", status.Value.Result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Engine_falls_back_to_rags_when_mandatory_graphrag_has_no_communities()
    {
        var sourceA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();
        var fallbackResults = new[]
        {
            new SearchResult(
                new Chunk(Guid.NewGuid(), sourceA, "RFP Alpha was registered in 2020 and remains an internal opportunity record.", 0),
                0.96f,
                new[] { sourceA.ToString() },
                retrievalStrategy: "semantic",
                rank: 1),
            new SearchResult(
                new Chunk(Guid.NewGuid(), sourceB, "RFP Beta was registered in 2024 and remains an internal opportunity record.", 0),
                0.94f,
                new[] { sourceB.ToString() },
                retrievalStrategy: "semantic",
                rank: 2)
        };
        var services = CreateServices(
            () => new RfpScenarioCopilotService("semantic"),
            rags: new FakeRagsService(fallbackResults),
            graphRag: new FailingGraphRagService("No communities detected in the graph."));
        var planResult = await services.Approval.CreatePlanAsync("Summarize registered RFP opportunities in the past 10 years.");
        Assert.True(planResult.IsSuccess);
        var plan = planResult.Value!;
        Assert.True(plan.RequiresToolCall);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", plan.ToolName);
        var approved = await services.Approval.ApproveAsync(plan.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, status.Value!.Status);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value!.Status);
        Assert.Null(progress.Value.Error);
        Assert.DoesNotContain(progress.Value.Messages, message => message.Message.Contains("Falling back to AletheiaKnowledgePlugin.SearchRags", StringComparison.OrdinalIgnoreCase));
            // Verify that the fallback scenario still includes the expected steps
            Assert.Contains(progress.Value.Steps, s => s.Name == "Retrieving context" && s.Status == ChatProgressStepStatus.Completed);
            Assert.Contains(progress.Value.Steps, s => s.Name == "Call repository tool" && s.Status == ChatProgressStepStatus.Completed);
            Assert.Contains(progress.Value.Steps, s => s.Name == "Verify tool returned internal context before synthesis" && s.Status == ChatProgressStepStatus.Completed);
            // The audit step (Synthesis) may appear but is not required for this fallback path
            Assert.DoesNotContain(progress.Value.Steps, s => s.Name == "Synthesis");
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", progress.Value.Telemetry!.ToolName);
        Assert.Equal(1, progress.Value.Telemetry.ToolInvocationCount);
        Assert.Equal("semantic", progress.Value.Telemetry.RetrievalStrategy);
        Assert.Contains("Registered RFP", status.Value.Result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Engine_hydrates_registered_rfp_sources_when_graphrag_and_broad_rags_return_no_context()
    {
        var sourceA = new KnowledgeSource(Guid.NewGuid(), "RFP Alpha.docx", DateTimeOffset.UtcNow.AddDays(-2));
        var sourceB = new KnowledgeSource(Guid.NewGuid(), "Request for Proposal Beta.pdf", DateTimeOffset.UtcNow.AddDays(-1));
        var sourceResults = new Dictionary<Guid, IReadOnlyList<SearchResult>>
        {
            [sourceA.SourceId] = new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), sourceA.SourceId, "RFP Alpha was registered in 2020 and includes operations support.", 0),
                    0.95f,
                    new[] { sourceA.SourceName },
                    retrievalStrategy: "semantic",
                    rank: 1)
            },
            [sourceB.SourceId] = new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), sourceB.SourceId, "RFP Beta was registered in 2024 and includes analytics support.", 0),
                    0.94f,
                    new[] { sourceB.SourceName },
                    retrievalStrategy: "semantic",
                    rank: 1)
            }
        };
        var rags = new HydratedSourceRagsService(sourceResults);
        var ingestion = new TrackingKnowledgeSourceIngestionService(source => rags.MarkHydrated(source.SourceId));
        var metadata = new FakeMetadataRepository(new[]
        {
            new FileMetadata(new FileDescriptor(sourceA.SourceId, sourceA.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1200, sourceA.UploadedAt),
            new FileMetadata(new FileDescriptor(sourceB.SourceId, sourceB.SourceName), "application/pdf", 1400, sourceB.UploadedAt)
        });
        var services = CreateServices(
            () => new RfpScenarioCopilotService("semantic"),
            rags: rags,
            graphRag: new FailingGraphRagService("No communities detected in the graph."),
            knowledgeSourceIngestion: ingestion,
            metadataRepository: metadata);
        var planResult = await services.Approval.CreatePlanAsync("Summarize registered RFP opportunities in the past 10 years.");
        Assert.True(planResult.IsSuccess);
        var approved = await services.Approval.ApproveAsync(planResult.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(planResult.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, status.Value!.Status);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value!.Status);
        Assert.Equal(2, ingestion.Sources.Count);
        Assert.Contains(ingestion.Sources, source => source.SourceId == sourceA.SourceId);
        Assert.Contains(ingestion.Sources, source => source.SourceId == sourceB.SourceId);
        Assert.Contains(progress.Value.Messages, message => message.Message.Contains("Dispatching to registered plugin", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", progress.Value.Telemetry!.ToolName);
        Assert.Equal("semantic", progress.Value.Telemetry.RetrievalStrategy);
    }

    [Fact]
    public async Task Engine_passes_lazy_scoped_rfp_context_to_synthesis()
    {
        var sourceA = new KnowledgeSource(Guid.NewGuid(), "RFP Alpha.docx", DateTimeOffset.UtcNow.AddDays(-2));
        var sourceB = new KnowledgeSource(Guid.NewGuid(), "RFP Beta.pdf", DateTimeOffset.UtcNow.AddDays(-1));
        var sourceResults = new Dictionary<Guid, IReadOnlyList<SearchResult>>
        {
            [sourceA.SourceId] = new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), sourceA.SourceId, "RFP Alpha asks for implementation support.", 0),
                    0.96f,
                    new[] { sourceA.SourceName },
                    retrievalStrategy: "semantic",
                    rank: 1)
            },
            [sourceB.SourceId] = new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), sourceB.SourceId, "RFP Beta asks for managed analytics support.", 0),
                    0.95f,
                    new[] { sourceB.SourceName },
                    retrievalStrategy: "semantic",
                    rank: 1)
            }
        };
        var copilot = new ContextCapturingCopilotService();
        var rags = new HydratedSourceRagsService(sourceResults);
        var ingestion = new TrackingKnowledgeSourceIngestionService(source => rags.MarkHydrated(source.SourceId));
        var metadata = new FakeMetadataRepository(new[]
        {
            new FileMetadata(new FileDescriptor(sourceA.SourceId, sourceA.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1200, sourceA.UploadedAt),
            new FileMetadata(new FileDescriptor(sourceB.SourceId, sourceB.SourceName), "application/pdf", 1400, sourceB.UploadedAt)
        });
        var services = CreateServices(
            () => copilot,
            rags: rags,
            graphRag: new FailingGraphRagService("No communities detected in the graph."),
            knowledgeSourceIngestion: ingestion,
            metadataRepository: metadata);
        var planResult = await services.Approval.CreatePlanAsync("Provide a summary list of all RFPs.");
        Assert.True(planResult.IsSuccess);
        var approved = await services.Approval.ApproveAsync(planResult.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(planResult.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, status.Value!.Status);
        Assert.NotNull(copilot.LastOptions);
        Assert.True(copilot.LastOptions!.UseProvidedRetrievalOnly);
        Assert.NotNull(copilot.LastOptions.RetrievalResults);
        Assert.Equal(2, copilot.LastOptions.RetrievalResults!.Select(result => result.Chunk.SourceId).Distinct().Count());
        Assert.Contains("lazy scoped WRAGS coverage", copilot.LastOptions.ScopeInstruction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Engine_skips_broad_rags_for_scoped_registered_sources()
    {
        var sourceA = new KnowledgeSource(Guid.NewGuid(), "RFP Alpha.docx", DateTimeOffset.UtcNow.AddDays(-2));
        var sourceB = new KnowledgeSource(Guid.NewGuid(), "RFP Beta.pdf", DateTimeOffset.UtcNow.AddDays(-1));
        var sourceResults = new Dictionary<Guid, IReadOnlyList<SearchResult>>
        {
            [sourceA.SourceId] = new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), sourceA.SourceId, "RFP Alpha includes AI advisory services.", 0),
                    0.96f,
                    new[] { sourceA.SourceName },
                    retrievalStrategy: "semantic")
            },
            [sourceB.SourceId] = new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), sourceB.SourceId, "RFP Beta includes AI enablement services.", 0),
                    0.95f,
                    new[] { sourceB.SourceName },
                    retrievalStrategy: "semantic")
            }
        };
        var copilot = new ContextCapturingCopilotService();
        var rags = new SourceOnlyRagsService(sourceResults);
        var metadata = new FakeMetadataRepository(new[]
        {
            new FileMetadata(new FileDescriptor(sourceA.SourceId, sourceA.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1200, sourceA.UploadedAt),
            new FileMetadata(new FileDescriptor(sourceB.SourceId, sourceB.SourceName), "application/pdf", 1400, sourceB.UploadedAt)
        });
        var services = CreateServices(
            () => copilot,
            rags: rags,
            metadataRepository: metadata);
        var planResult = await services.Approval.CreatePlanAsync("Provide a summary list of all RFPs.");
        Assert.True(planResult.IsSuccess);
        var approved = await services.Approval.ApproveAsync(planResult.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(planResult.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value!.Status);
        Assert.Equal(0, rags.BroadCalls);
        Assert.Equal(2, rags.SourceCalls);
        Assert.NotNull(copilot.LastOptions?.RetrievalResults);
        Assert.Equal(2, copilot.LastOptions!.RetrievalResults!.Select(result => result.Chunk.SourceId).Distinct().Count());
    }

    [Fact]
    public async Task Engine_completes_instantly_on_small_corpus()
    {
        var options = new ChatExecutionEngineOptions
        {
            DefaultStepTimeoutSeconds = 30,
            MandatoryToolTimeoutSeconds = 30,
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
    public async Task Engine_source_scoped_fallback_uses_indexed_chunks_before_hydration()
    {
        var sourceA = new KnowledgeSource(Guid.NewGuid(), "RFP Analysis Alpha.docx", DateTimeOffset.UtcNow.AddDays(-2));
        var sourceB = new KnowledgeSource(Guid.NewGuid(), "RFP Analysis Beta.docx", DateTimeOffset.UtcNow.AddDays(-1));
        var sourceResults = new Dictionary<Guid, IReadOnlyList<SearchResult>>
        {
            [sourceA.SourceId] = new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), sourceA.SourceId, "RFP Analysis Alpha evaluates AI workflow automation opportunities.", 0),
                    0.96f,
                    new[] { sourceA.SourceName },
                    retrievalStrategy: "semantic")
            },
            [sourceB.SourceId] = new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), sourceB.SourceId, "RFP Analysis Beta evaluates managed analytics and reporting engagements.", 0),
                    0.95f,
                    new[] { sourceB.SourceName },
                    retrievalStrategy: "semantic")
            }
        };
        var copilot = new ContextCapturingCopilotService();
        var rags = new SourceOnlyRagsService(sourceResults);
        var metadata = new FakeMetadataRepository(new[]
        {
            new FileMetadata(new FileDescriptor(sourceA.SourceId, sourceA.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1200, sourceA.UploadedAt),
            new FileMetadata(new FileDescriptor(sourceB.SourceId, sourceB.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1400, sourceB.UploadedAt)
        });
        var ingestion = new HangingKnowledgeSourceIngestionService();
        var options = new ChatExecutionEngineOptions
        {
            DefaultStepTimeoutSeconds = 30,
            MandatoryToolTimeoutSeconds = 30,
            OverallJobTimeoutSeconds = 60,
            HeartbeatIntervalSeconds = 1,
            LongWaitHeartbeatIntervalSeconds = 2,
            HeartbeatWatchdogMissedThreshold = 5,
            SmallCorpusDocumentThreshold = 5,
            SmallCorpusTimeoutSeconds = 5,
            HydrationTimeoutSeconds = 2
        };
        var emptyInvoker = new FakeChatToolInvoker(rags, error: "Plugin returned no results.");
        var services = CreateServices(
            () => copilot,
            rags: rags,
            knowledgeSourceIngestion: ingestion,
            metadataRepository: metadata,
            toolInvoker: emptyInvoker,
            options: options);

        var planResult = await services.Approval.CreatePlanAsync("summarize the purpose of each of the RFP analysis engagements");
        Assert.True(planResult.IsSuccess);
        var approved = await services.Approval.ApproveAsync(planResult.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(planResult.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(10));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, status.Value!.Status);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value!.Status);
        Assert.Contains(progress.Value.Messages, message => message.Message.Contains("already-indexed chunk", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(progress.Value.Messages, message => message.Message.Contains("hydration timed out", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, rags.SourceCalls);
        Assert.Equal(0, rags.BroadCalls);
        Assert.Equal(0, ingestion.CallCount);
    }

    [Fact]
    public async Task Engine_summarizes_rfp_engagements_fast_for_two_document_corpus()
    {
        var sourceA = new KnowledgeSource(Guid.NewGuid(), "RFP Analysis Alpha.docx", DateTimeOffset.UtcNow.AddDays(-2));
        var sourceB = new KnowledgeSource(Guid.NewGuid(), "RFP Analysis Beta.docx", DateTimeOffset.UtcNow.AddDays(-1));
        var sourceResults = new Dictionary<Guid, IReadOnlyList<SearchResult>>
        {
            [sourceA.SourceId] = new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), sourceA.SourceId, "RFP Analysis Alpha evaluates AI workflow automation opportunities.", 0),
                    0.96f,
                    new[] { sourceA.SourceName },
                    retrievalStrategy: "semantic")
            },
            [sourceB.SourceId] = new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), sourceB.SourceId, "RFP Analysis Beta evaluates managed analytics and reporting engagements.", 0),
                    0.95f,
                    new[] { sourceB.SourceName },
                    retrievalStrategy: "semantic")
            }
        };
        var copilot = new ContextCapturingCopilotService();
        var rags = new SourceOnlyRagsService(sourceResults);
        var metadata = new FakeMetadataRepository(new[]
        {
            new FileMetadata(new FileDescriptor(sourceA.SourceId, sourceA.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1200, sourceA.UploadedAt),
            new FileMetadata(new FileDescriptor(sourceB.SourceId, sourceB.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1400, sourceB.UploadedAt)
        });
        var graphRag = new AssertNeverCalledGraphRagService();
        var lazyGraphRag = new AssertNeverCalledLazyGraphRagService();
        var invoker = new FakeChatToolInvoker(rags);
        var options = new ChatExecutionEngineOptions
        {
            DefaultStepTimeoutSeconds = 30,
            MandatoryToolTimeoutSeconds = 30,
            OverallJobTimeoutSeconds = 60,
            HeartbeatIntervalSeconds = 1,
            LongWaitHeartbeatIntervalSeconds = 2,
            HeartbeatWatchdogMissedThreshold = 5,
            SmallCorpusDocumentThreshold = 5,
            SmallCorpusTimeoutSeconds = 5
        };
        var services = CreateServices(
            () => copilot,
            rags: rags,
            graphRag: graphRag,
            lazyGraphRag: lazyGraphRag,
            metadataRepository: metadata,
            toolInvoker: invoker,
            options: options);

        var planResult = await services.Approval.CreatePlanAsync("summarize the purpose of each of the RFP analysis engagements");
        Assert.True(planResult.IsSuccess);
        var plan = planResult.Value!;
        Assert.True(plan.RequiresToolCall);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", plan.ToolName);

        var approved = await services.Approval.ApproveAsync(plan.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.PlanId);
        Assert.True(started.IsSuccess);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(10));
        sw.Stop();

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, status.Value!.Status);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value!.Status);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Two-document RFP summary took {sw.ElapsedMilliseconds}ms");
        Assert.Equal(0, rags.BroadCalls);
        Assert.Equal(2, rags.SourceCalls);
        Assert.False(graphRag.WasCalled);
        Assert.False(lazyGraphRag.WasCalled);
        Assert.NotNull(copilot.LastOptions?.RetrievalResults);
        Assert.Equal(2, copilot.LastOptions!.RetrievalResults!.Select(result => result.Chunk.SourceId).Distinct().Count());
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", progress.Value.Telemetry!.ToolName);
        Assert.Equal("semantic", progress.Value.Telemetry.RetrievalStrategy);
    }

    [Fact]
    public async Task Engine_uses_registered_source_metadata_when_rags_chunks_are_missing()
    {
        var sourceA = new KnowledgeSource(Guid.NewGuid(), "CMP 2026 - 3. RFP Analysis.docx", DateTimeOffset.UtcNow.AddMonths(-2));
        var sourceB = new KnowledgeSource(Guid.NewGuid(), "CMP 2022 - 3. RFP Analysis.docx", DateTimeOffset.UtcNow.AddYears(-4));
        var copilot = new ContextCapturingCopilotService();
        var rags = new SourceOnlyRagsService(new Dictionary<Guid, IReadOnlyList<SearchResult>>());
        var metadata = new FakeMetadataRepository(new[]
        {
            new FileMetadata(new FileDescriptor(sourceA.SourceId, sourceA.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1200, sourceA.UploadedAt),
            new FileMetadata(new FileDescriptor(sourceB.SourceId, sourceB.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1400, sourceB.UploadedAt)
        });
        var ingestion = new HangingKnowledgeSourceIngestionService();
        var options = new ChatExecutionEngineOptions
        {
            DefaultStepTimeoutSeconds = 2,
            MandatoryToolTimeoutSeconds = 30,
            OverallJobTimeoutSeconds = 60,
            HeartbeatIntervalSeconds = 1,
            LongWaitHeartbeatIntervalSeconds = 2,
            HeartbeatWatchdogMissedThreshold = 5,
            SmallCorpusDocumentThreshold = 5,
            SmallCorpusTimeoutSeconds = 1,
            HydrationTimeoutSeconds = 1
        };
        var invoker = new FakeChatToolInvoker(rags, error: "Plugin returned no results.");
        var services = CreateServices(
            () => copilot,
            rags: rags,
            knowledgeSourceIngestion: ingestion,
            metadataRepository: metadata,
            toolInvoker: invoker,
            options: options);

        var planResult = await services.Approval.CreatePlanAsync("summarize RFP opportunities available in the past 5 years");
        Assert.True(planResult.IsSuccess);
        var approved = await services.Approval.ApproveAsync(planResult.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(planResult.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(10));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, status.Value!.Status);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value!.Status);
        Assert.Contains(progress.Value.Messages, message => message.Message.Contains("repository metadata record", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(copilot.LastOptions?.RetrievalResults);
        Assert.Equal(2, copilot.LastOptions!.RetrievalResults!.Count);
        Assert.All(copilot.LastOptions.RetrievalResults!, result => Assert.Equal("metadata", result.RetrievalStrategy));
        Assert.Contains(copilot.LastOptions.RetrievalResults!, result => result.Citations.Contains(sourceA.SourceName));
        Assert.Contains(copilot.LastOptions.RetrievalResults!, result => result.Citations.Contains(sourceB.SourceName));
    }

    [Fact]
    public async Task Engine_invokes_legacy_graphrag_tool_path_then_falls_back_to_rags()
    {
        var sourceA = new KnowledgeSource(Guid.NewGuid(), "RFP Alpha.docx", DateTimeOffset.UtcNow.AddDays(-2));
        var sourceB = new KnowledgeSource(Guid.NewGuid(), "RFP Beta.pdf", DateTimeOffset.UtcNow.AddDays(-1));
        var sourceResults = new Dictionary<Guid, IReadOnlyList<SearchResult>>
        {
            [sourceA.SourceId] = new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), sourceA.SourceId, "RFP Alpha asks for implementation support.", 0),
                    0.96f,
                    new[] { sourceA.SourceName },
                    retrievalStrategy: "semantic")
            },
            [sourceB.SourceId] = new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), sourceB.SourceId, "RFP Beta asks for managed analytics support.", 0),
                    0.95f,
                    new[] { sourceB.SourceName },
                    retrievalStrategy: "semantic")
            }
        };
        var copilot = new ContextCapturingCopilotService();
        var rags = new SourceOnlyRagsService(sourceResults);
        var metadata = new FakeMetadataRepository(new[]
        {
            new FileMetadata(new FileDescriptor(sourceA.SourceId, sourceA.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1200, sourceA.UploadedAt),
            new FileMetadata(new FileDescriptor(sourceB.SourceId, sourceB.SourceName), "application/pdf", 1400, sourceB.UploadedAt)
        });
        var graphRag = new AssertNeverCalledGraphRagService();
        var lazyGraphRag = new AssertNeverCalledLazyGraphRagService();
        var invoker = new FakeChatToolInvoker(rags);
        var options = new ChatExecutionEngineOptions
        {
            DefaultStepTimeoutSeconds = 30,
            MandatoryToolTimeoutSeconds = 30,
            OverallJobTimeoutSeconds = 60,
            HeartbeatIntervalSeconds = 1,
            LongWaitHeartbeatIntervalSeconds = 2,
            HeartbeatWatchdogMissedThreshold = 5,
            SmallCorpusDocumentThreshold = 5,
            SmallCorpusTimeoutSeconds = 5
        };
        var services = CreateServices(
            () => copilot,
            rags: rags,
            graphRag: graphRag,
            lazyGraphRag: lazyGraphRag,
            metadataRepository: metadata,
            toolInvoker: invoker,
            options: options);

        var plan = await services.Approval.CreatePlanAsync("Provide a summary list of all RFPs.");
        Assert.True(plan.IsSuccess);
        var approved = await services.Approval.ApproveAsync(plan.Value!.PlanId);
        Assert.True(approved.IsSuccess);

        // Simulate a legacy GraphRAG tool plan reaching the engine.
        var repositoryField = services.Approval.GetType().GetField("_repository", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(repositoryField);
        var repository = repositoryField.GetValue(services.Approval);
        Assert.NotNull(repository);
        var getMethod = repository!.GetType().GetMethod("GetAsync", new[] { typeof(Guid), typeof(CancellationToken) });
        Assert.NotNull(getMethod);
        var getTask = (Task<Result<ChatPlanRecord?>>?)getMethod!.Invoke(repository, new object[] { plan.Value.PlanId, CancellationToken.None });
        Assert.NotNull(getTask);
        var getResult = await getTask;
        Assert.True(getResult.IsSuccess);
        Assert.NotNull(getResult.Value);
        var existing = getResult.Value!;
        var graphPlanRecord = new ChatPlanRecord
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
            RequiresToolCall = true,
            ToolName = "AletheiaKnowledgePlugin.SearchGraphRag",
            ToolArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["query"] = existing.Prompt,
                ["topK"] = existing.EstimatedRetrievalCount.ToString()
            },
            ReviewedBy = existing.ReviewedBy,
            ReviewedAt = existing.ReviewedAt,
            CreatedAt = existing.CreatedAt,
            ExpiresAt = existing.ExpiresAt,
            CancellationReason = existing.CancellationReason
        };
        var saveMethod = repository.GetType().GetMethod("SaveAsync", new[] { typeof(ChatPlanRecord), typeof(CancellationToken) });
        Assert.NotNull(saveMethod);
        var saveTask = (Task<Result>?)saveMethod!.Invoke(repository, new object[] { graphPlanRecord, CancellationToken.None });
        Assert.NotNull(saveTask);
        var saveResult = await saveTask;
        Assert.True(saveResult.IsSuccess);

        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(10));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, status.Value!.Status);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value!.Status);
        Assert.False(graphRag.WasCalled);
        Assert.False(lazyGraphRag.WasCalled);
        Assert.Equal("AletheiaKnowledgePlugin.SearchGraphRag", invoker.LastToolName);
        Assert.Equal("AletheiaKnowledgePlugin.SearchGraphRag", progress.Value.Telemetry!.ToolName);
        Assert.Contains(progress.Value.Messages, message => message.Message.Contains("Falling back to AletheiaKnowledgePlugin.SearchRags", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(progress.Value.Messages, message => message.Message.Contains("Sending request to chat agent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Engine_clamps_fast_path_zero_retrieval_count_before_retrieval()
    {
        var sourceId = Guid.NewGuid();
        var rags = new ToolCapturingRagsService(new[]
        {
            new SearchResult(new Chunk(Guid.NewGuid(), sourceId, "small answer context", 0), 0.9f)
        });
        var services = CreateServices(rags: rags);
        var plan = await services.Approval.CreatePlanAsync("hello");
        Assert.True(plan.IsSuccess);
        Assert.Equal(0, plan.Value!.EstimatedRetrievalCount);
        var approved = await services.Approval.ApproveAsync(plan.Value.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, status.Value!.Status);
        Assert.NotNull(rags.LastRequest);
        Assert.Equal(1, rags.LastRequest!.TopK);
    }

    [Fact]
    public async Task Engine_uses_rags_document_context_for_cmp_feature_request()
    {
        var sourceId = Guid.NewGuid();
        var copilot = new ContextCapturingCopilotService();
        var rags = new ToolCapturingRagsService(new[]
        {
            new SearchResult(
                new Chunk(Guid.NewGuid(), sourceId, "CMP 2026 required features include AI-assisted workflow intake, reporting dashboards, and audit exports.", 12),
                0.93f,
                new[] { "CMP 2026 - 3. RFP Analysis.docx" },
                retrievalStrategy: "semantic")
        });
        var services = CreateServices(
            () => copilot,
            rags: rags,
            graphRag: new FailingGraphRagService("Graph summaries should not be used for scoped feature requests."));
        var plan = await services.Approval.CreatePlanAsync("Base on CMP 2026 list required features for this engagement");
        Assert.True(plan.IsSuccess);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", plan.Value!.ToolName);
        var approved = await services.Approval.ApproveAsync(plan.Value.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value!.Status);
        Assert.NotNull(rags.LastRequest);
        Assert.Equal("Base on CMP 2026 list required features for this engagement", rags.LastRequest!.Query);
        Assert.NotNull(copilot.LastOptions);
        Assert.True(copilot.LastOptions!.UseProvidedRetrievalOnly);
        Assert.NotNull(copilot.LastOptions.RetrievalResults);
        Assert.Single(copilot.LastOptions.RetrievalResults!);
        Assert.Contains("document-level evidence", copilot.LastOptions.ScopeInstruction);
        Assert.Contains("do not mention graph communities", copilot.LastOptions.ScopeInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", progress.Value.Telemetry!.ToolName);
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
    public async Task Engine_invokes_registered_repository_plugin_for_mandatory_tool()
    {
        var sourceId = Guid.NewGuid();
        var rags = new FakeRagsService(new[]
        {
            new SearchResult(new Chunk(Guid.NewGuid(), sourceId, "plugin-retrieved context", 0), 0.92f)
        });
        var invoker = new FakeChatToolInvoker(rags);
        var services = CreateServices(
            rags: rags,
            toolInvoker: invoker);

        var planResult = await services.Approval.CreatePlanAsync("what does the CMP RFP require?");
        Assert.True(planResult.IsSuccess);
        var plan = planResult.Value!;
        Assert.True(plan.RequiresToolCall);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", plan.ToolName);

        var approved = await services.Approval.ApproveAsync(plan.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(plan.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, status.Value!.Status);
        Assert.True(progress.IsSuccess);
        Assert.NotNull(progress.Value);
        Assert.Contains(progress.Value!.Messages, message =>
            message.Message.Contains("Dispatching to registered plugin", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", invoker.LastToolName);
        Assert.Equal(1, invoker.InvokeCount);
    }

    [Fact]
    public async Task Engine_accepts_RepositoryTool_tool_name()
    {
        var sourceId = Guid.NewGuid();
        var rags = new FakeRagsService(new[]
        {
            new SearchResult(new Chunk(Guid.NewGuid(), sourceId, "repository tool context", 0), 0.91f)
        });
        var invoker = new FakeChatToolInvoker(rags);
        var services = CreateServices(
            rags: rags,
            toolInvoker: invoker);

        var planResult = await services.Approval.CreatePlanAsync("what does the CMP RFP require?");
        Assert.True(planResult.IsSuccess);
        var plan = planResult.Value!;

        // Mutate plan to use the RepositoryTool alias.
        var repositoryField = services.Approval.GetType().GetField("_repository", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(repositoryField);
        var repository = repositoryField.GetValue(services.Approval);
        Assert.NotNull(repository);
        var getMethod = repository!.GetType().GetMethod("GetAsync", new[] { typeof(Guid), typeof(CancellationToken) });
        Assert.NotNull(getMethod);
        var getTask = (Task<Result<ChatPlanRecord?>>?)getMethod!.Invoke(repository, new object[] { plan.PlanId, CancellationToken.None });
        Assert.NotNull(getTask);
        var getResult = await getTask;
        Assert.True(getResult.IsSuccess);
        Assert.NotNull(getResult.Value);
        var existing = getResult.Value!;
        var repositoryToolPlan = new ChatPlanRecord
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
            RequiresToolCall = true,
            ToolName = "RepositoryTool.SearchRepositoryDocuments",
            ToolArguments = existing.ToolArguments,
            ReviewedBy = existing.ReviewedBy,
            ReviewedAt = existing.ReviewedAt,
            CreatedAt = existing.CreatedAt,
            ExpiresAt = existing.ExpiresAt,
            CancellationReason = existing.CancellationReason
        };
        var saveMethod = repository.GetType().GetMethod("SaveAsync", new[] { typeof(ChatPlanRecord), typeof(CancellationToken) });
        Assert.NotNull(saveMethod);
        var saveTask = (Task<Result>?)saveMethod!.Invoke(repository, new object[] { repositoryToolPlan, CancellationToken.None });
        Assert.NotNull(saveTask);
        var saveResult = await saveTask;
        Assert.True(saveResult.IsSuccess);

        var approved = await services.Approval.ApproveAsync(repositoryToolPlan.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(repositoryToolPlan.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, status.Value!.Status);
        Assert.Equal("RepositoryTool.SearchRepositoryDocuments", invoker.LastToolName);
    }

    [Fact]
    public async Task Engine_enforces_repository_lookup_when_behavior_flag_set_and_no_tool_required()
    {
        var sourceId = Guid.NewGuid();
        var rags = new FakeRagsService(new[]
        {
            new SearchResult(new Chunk(Guid.NewGuid(), sourceId, "behavior-enforced context", 0), 0.93f)
        });
        var invoker = new FakeChatToolInvoker(rags);
        var chatAgentOptions = new ChatAgentOptions
        {
            BehaviorFlags = new ChatAgentBehaviorFlags { RequireRepositoryLookupBeforeAnswer = true },
            ToolNames = new ChatAgentToolNames { SearchRepository = "AletheiaKnowledgePlugin.SearchRags" }
        };
        var services = CreateServices(
            rags: rags,
            toolInvoker: invoker,
            chatAgentOptions: chatAgentOptions);

        var planResult = await services.Approval.CreatePlanAsync("what does the CMP RFP require?");
        Assert.True(planResult.IsSuccess);
        var plan = planResult.Value!;

        var repositoryField = services.Approval.GetType().GetField("_repository", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(repositoryField);
        var repository = repositoryField.GetValue(services.Approval);
        Assert.NotNull(repository);
        var getMethod = repository!.GetType().GetMethod("GetAsync", new[] { typeof(Guid), typeof(CancellationToken) });
        Assert.NotNull(getMethod);
        var getTask = (Task<Result<ChatPlanRecord?>>?)getMethod!.Invoke(repository, new object[] { plan.PlanId, CancellationToken.None });
        Assert.NotNull(getTask);
        var getResult = await getTask;
        Assert.True(getResult.IsSuccess);
        Assert.NotNull(getResult.Value);
        var existing = getResult.Value!;
        var noToolPlan = new ChatPlanRecord
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
            RequiresToolCall = false,
            ToolName = string.Empty,
            ToolArguments = existing.ToolArguments,
            ReviewedBy = existing.ReviewedBy,
            ReviewedAt = existing.ReviewedAt,
            CreatedAt = existing.CreatedAt,
            ExpiresAt = existing.ExpiresAt,
            CancellationReason = existing.CancellationReason
        };
        var saveMethod = repository.GetType().GetMethod("SaveAsync", new[] { typeof(ChatPlanRecord), typeof(CancellationToken) });
        Assert.NotNull(saveMethod);
        var saveTask = (Task<Result>?)saveMethod!.Invoke(repository, new object[] { noToolPlan, CancellationToken.None });
        Assert.NotNull(saveTask);
        var saveResult = await saveTask;
        Assert.True(saveResult.IsSuccess);

        var approved = await services.Approval.ApproveAsync(noToolPlan.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(noToolPlan.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(5));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, status.Value!.Status);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", invoker.LastToolName);
        Assert.True(invoker.InvokeCount >= 1);
    }

    [Fact]
    public async Task Engine_hanging_plugin_fails_fast_with_plugin_name()
    {
        var options = new ChatExecutionEngineOptions
        {
            DefaultStepTimeoutSeconds = 1,
            MandatoryToolTimeoutSeconds = 2,
            OverallJobTimeoutSeconds = 10,
            HeartbeatIntervalSeconds = 1,
            LongWaitHeartbeatIntervalSeconds = 1,
            HeartbeatWatchdogMissedThreshold = 5,
            SmallCorpusDocumentThreshold = 5,
            SmallCorpusTimeoutSeconds = 5
        };
        var services = CreateServices(
            rags: new HangingRagsService(),
            toolInvoker: new HangingChatToolInvoker(),
            options: options);

        var planResult = await services.Approval.CreatePlanAsync("what does the CMP RFP require?");
        Assert.True(planResult.IsSuccess);
        var approved = await services.Approval.ApproveAsync(planResult.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(planResult.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(10));

        var status = await services.Execution.GetStatusAsync(started.Value.JobId);
        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(status.IsSuccess);
        Assert.Equal(ChatJobStatus.Failed, status.Value!.Status);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Failed, progress.Value!.Status);
        Assert.Contains("AletheiaKnowledgePlugin.SearchRags", progress.Value.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("timed out", progress.Value.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Engine_scopes_year_qualified_prompt_to_single_document()
    {
        var source2022 = new KnowledgeSource(Guid.NewGuid(), "CMP 2022 - 3. RFP Analysis.docx", DateTimeOffset.UtcNow.AddDays(-2));
        var source2026 = new KnowledgeSource(Guid.NewGuid(), "CMP 2026 - 3. RFP Analysis.docx", DateTimeOffset.UtcNow.AddDays(-1));
        var sourceResults = new Dictionary<Guid, IReadOnlyList<SearchResult>>
        {
            [source2022.SourceId] = new[]
            {
                new SearchResult(new Chunk(Guid.NewGuid(), source2022.SourceId, "CMP 2022 requirements and scope.", 0), 0.9f, new[] { source2022.SourceName }, retrievalStrategy: "semantic", rank: 1)
            },
            [source2026.SourceId] = new[]
            {
                new SearchResult(new Chunk(Guid.NewGuid(), source2026.SourceId, "CMP 2026 requirements and scope.", 0), 0.95f, new[] { source2026.SourceName }, retrievalStrategy: "semantic", rank: 1)
            }
        };
        var copilot = new ContextCapturingCopilotService();
        var rags = new SourceOnlyRagsService(sourceResults);
        var metadata = new FakeMetadataRepository(new[]
        {
            new FileMetadata(new FileDescriptor(source2022.SourceId, source2022.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 57028, source2022.UploadedAt),
            new FileMetadata(new FileDescriptor(source2026.SourceId, source2026.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 57029, source2026.UploadedAt)
        });
        var services = CreateServices(() => copilot, rags: rags, metadataRepository: metadata);

        var planResult = await services.Approval.CreatePlanAsync("provide project details about CMP 2026 RFP");
        Assert.True(planResult.IsSuccess);
        var approved = await services.Approval.ApproveAsync(planResult.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(planResult.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(10));

        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value!.Status);
        Assert.Equal(0, rags.BroadCalls);
        Assert.Equal(1, rags.SourceCalls);
        Assert.NotNull(copilot.LastOptions?.RetrievalResults);
        var sourceIds = copilot.LastOptions!.RetrievalResults!.Select(result => result.Chunk.SourceId).Distinct().ToList();
        Assert.Single(sourceIds);
        Assert.Equal(source2026.SourceId, sourceIds[0]);
    }

    [Fact]
    public async Task Engine_retrieves_each_cmp_project_independently_for_summary_prompt()
    {
        var source2022 = new KnowledgeSource(Guid.NewGuid(), "CMP 2022 - 3. RFP Analysis.docx", DateTimeOffset.UtcNow.AddDays(-2));
        var source2026 = new KnowledgeSource(Guid.NewGuid(), "CMP 2026 - 3. RFP Analysis.docx", DateTimeOffset.UtcNow.AddDays(-1));
        var sourceResults = new Dictionary<Guid, IReadOnlyList<SearchResult>>
        {
            [source2022.SourceId] = new[]
            {
                new SearchResult(new Chunk(Guid.NewGuid(), source2022.SourceId, "CMP 2022 project summary.", 0), 0.9f, new[] { source2022.SourceName }, retrievalStrategy: "semantic", rank: 1)
            },
            [source2026.SourceId] = new[]
            {
                new SearchResult(new Chunk(Guid.NewGuid(), source2026.SourceId, "CMP 2026 project summary.", 0), 0.95f, new[] { source2026.SourceName }, retrievalStrategy: "semantic", rank: 1)
            }
        };
        var copilot = new ContextCapturingCopilotService();
        var rags = new SourceOnlyRagsService(sourceResults);
        var metadata = new FakeMetadataRepository(new[]
        {
            new FileMetadata(new FileDescriptor(source2022.SourceId, source2022.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 57028, source2022.UploadedAt),
            new FileMetadata(new FileDescriptor(source2026.SourceId, source2026.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 57029, source2026.UploadedAt)
        });
        var services = CreateServices(() => copilot, rags: rags, metadataRepository: metadata);

        var planResult = await services.Approval.CreatePlanAsync("provide summary of CMP projects");
        Assert.True(planResult.IsSuccess);
        var approved = await services.Approval.ApproveAsync(planResult.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(planResult.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(10));

        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value!.Status);
        Assert.Equal(0, rags.BroadCalls);
        Assert.Equal(2, rags.SourceCalls);
        Assert.NotNull(copilot.LastOptions?.RetrievalResults);
        var sourceIds = copilot.LastOptions!.RetrievalResults!.Select(result => result.Chunk.SourceId).Distinct().ToList();
        Assert.Equal(2, sourceIds.Count);
        Assert.Contains(source2022.SourceId, sourceIds);
        Assert.Contains(source2026.SourceId, sourceIds);
    }

    [Fact]
    public async Task Engine_keeps_generic_rfp_prompt_unscoped()
    {
        var sourceA = new KnowledgeSource(Guid.NewGuid(), "CMP 2022 - 3. RFP Analysis.docx", DateTimeOffset.UtcNow.AddDays(-2));
        var results = new[]
        {
            new SearchResult(new Chunk(Guid.NewGuid(), sourceA.SourceId, "Generic RFP guidance.", 0), 0.9f, new[] { sourceA.SourceName }, retrievalStrategy: "semantic", rank: 1)
        };
        var rags = new RecordingRagsService(results);
        var metadata = new FakeMetadataRepository(new[]
        {
            new FileMetadata(new FileDescriptor(sourceA.SourceId, sourceA.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 57028, sourceA.UploadedAt)
        });
        var services = CreateServices(rags: rags, metadataRepository: metadata);

        var planResult = await services.Approval.CreatePlanAsync("what is an RFP");
        Assert.True(planResult.IsSuccess);
        var approved = await services.Approval.ApproveAsync(planResult.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(planResult.Value.PlanId);
        Assert.True(started.IsSuccess);

        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(10));

        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value!.Status);
        Assert.NotEmpty(rags.Requests);
        Assert.All(rags.Requests, sourceId => Assert.Null(sourceId));
    }

    [Fact]
    public async Task Engine_uses_prior_session_sources_for_followup_prompt()
    {
        var sessionId = Guid.NewGuid();
        var source2022 = new KnowledgeSource(Guid.NewGuid(), "CMP 2022 - 3. RFP Analysis.docx", DateTimeOffset.UtcNow.AddDays(-2));
        var source2026 = new KnowledgeSource(Guid.NewGuid(), "CMP 2026 - 3. RFP Analysis.docx", DateTimeOffset.UtcNow.AddDays(-1));
        var sourceResults = new Dictionary<Guid, IReadOnlyList<SearchResult>>
        {
            [source2022.SourceId] = new[]
            {
                new SearchResult(new Chunk(Guid.NewGuid(), source2022.SourceId, "CMP 2022 project summary.", 0), 0.9f, new[] { source2022.SourceName }, retrievalStrategy: "semantic", rank: 1)
            },
            [source2026.SourceId] = new[]
            {
                new SearchResult(new Chunk(Guid.NewGuid(), source2026.SourceId, "CMP 2026 project summary.", 0), 0.95f, new[] { source2026.SourceName }, retrievalStrategy: "semantic", rank: 1)
            }
        };
        var copilot = new ContextCapturingCopilotService();
        var rags = new SourceOnlyRagsService(sourceResults);
        var metadata = new FakeMetadataRepository(new[]
        {
            new FileMetadata(new FileDescriptor(source2022.SourceId, source2022.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 57028, source2022.UploadedAt),
            new FileMetadata(new FileDescriptor(source2026.SourceId, source2026.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 57029, source2026.UploadedAt)
        });
        var services = CreateServices(() => copilot, rags: rags, metadataRepository: metadata);

        var firstPlan = await services.Approval.CreatePlanAsync("provide summary of CMP projects", sessionId: sessionId);
        Assert.True(firstPlan.IsSuccess);
        var firstApproved = await services.Approval.ApproveAsync(firstPlan.Value!.PlanId);
        Assert.True(firstApproved.IsSuccess);
        var firstStarted = await services.Execution.StartAsync(firstPlan.Value.PlanId);
        Assert.True(firstStarted.IsSuccess);
        await services.RunUntilTerminalAsync(firstStarted.Value!.JobId, TimeSpan.FromSeconds(10));

        var secondPlan = await services.Approval.CreatePlanAsync("what is the nature of the second one", sessionId: sessionId);
        Assert.True(secondPlan.IsSuccess);
        var secondApproved = await services.Approval.ApproveAsync(secondPlan.Value!.PlanId);
        Assert.True(secondApproved.IsSuccess);
        var secondStarted = await services.Execution.StartAsync(secondPlan.Value.PlanId);
        Assert.True(secondStarted.IsSuccess);
        await services.RunUntilTerminalAsync(secondStarted.Value!.JobId, TimeSpan.FromSeconds(10));

        var progress = await services.Execution.GetProgressAsync(secondStarted.Value.JobId);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value!.Status);
        Assert.Equal(0, rags.BroadCalls);
        Assert.Equal(4, rags.SourceCalls);
        Assert.NotNull(copilot.LastOptions?.RetrievalResults);
        var sourceIds = copilot.LastOptions!.RetrievalResults!.Select(result => result.Chunk.SourceId).Distinct().ToList();
        Assert.Equal(2, sourceIds.Count);
        Assert.Contains(source2022.SourceId, sourceIds);
        Assert.Contains(source2026.SourceId, sourceIds);
    }

    [Fact]
    public async Task Engine_passes_session_history_to_synthesis()
    {
        var sessionId = Guid.NewGuid();
        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = "list the CMP RFPs" },
            new() { Role = "assistant", Content = "Two CMP RFPs found." }
        };
        var copilot = new SessionCapturingCopilotService();
        var rags = new FakeRagsService(new[]
        {
            new SearchResult(new Chunk(Guid.NewGuid(), Guid.NewGuid(), "context", 0), 0.9f)
        });
        var services = CreateServices(() => copilot, rags: rags);

        var planResult = await services.Approval.CreatePlanAsync("what is the nature of these RFPs", sessionId: sessionId, history: history);
        Assert.True(planResult.IsSuccess);
        var approved = await services.Approval.ApproveAsync(planResult.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(planResult.Value.PlanId);
        Assert.True(started.IsSuccess);
        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(10));

        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value!.Status);
        Assert.NotNull(copilot.LastSession);
        Assert.Equal(sessionId, copilot.LastSession!.Id);
        Assert.Equal(2, copilot.LastSession!.Messages.Count);
        Assert.Equal("list the CMP RFPs", copilot.LastSession.Messages[0].Content);
    }

    [Fact]
    public async Task Engine_injects_opening_chunks_and_section_outline_for_template_documents()
    {
        var source2022 = new KnowledgeSource(Guid.NewGuid(), "CMP 2022 - 3. RFP Analysis.docx", DateTimeOffset.UtcNow.AddDays(-2));
        var source2026 = new KnowledgeSource(Guid.NewGuid(), "CMP 2026 - 3. RFP Analysis.docx", DateTimeOffset.UtcNow.AddDays(-1));
        var sourceResults = new Dictionary<Guid, IReadOnlyList<SearchResult>>
        {
            [source2022.SourceId] = new[]
            {
                new SearchResult(new Chunk(Guid.NewGuid(), source2022.SourceId, "Project Summary: Cleveland Metroparks sought a vendor for a data analytics platform.", 0), 0.9f, new[] { source2022.SourceName }, retrievalStrategy: "semantic", rank: 1),
                new SearchResult(new Chunk(Guid.NewGuid(), source2022.SourceId, "Scope of work details for 2022.", 1), 0.8f, new[] { source2022.SourceName }, retrievalStrategy: "semantic", rank: 2)
            },
            [source2026.SourceId] = new[]
            {
                new SearchResult(new Chunk(Guid.NewGuid(), source2026.SourceId, "Project Summary: Cleveland Metroparks sought a vendor for a customer data platform in 2026.", 0), 0.95f, new[] { source2026.SourceName }, retrievalStrategy: "semantic", rank: 1),
                new SearchResult(new Chunk(Guid.NewGuid(), source2026.SourceId, "Scope of work details for 2026.", 1), 0.85f, new[] { source2026.SourceName }, retrievalStrategy: "semantic", rank: 2)
            }
        };
        var copilot = new ContextCapturingCopilotService();
        var rags = new SourceOnlyRagsService(sourceResults);
        var metadata = new FakeMetadataRepository(new[]
        {
            new FileMetadata(new FileDescriptor(source2022.SourceId, source2022.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 57028, source2022.UploadedAt),
            new FileMetadata(new FileDescriptor(source2026.SourceId, source2026.SourceName), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 57029, source2026.UploadedAt)
        });
        var services = CreateServices(() => copilot, rags: rags, metadataRepository: metadata, templateRegistry: new DocumentTemplateRegistry());

        var planResult = await services.Approval.CreatePlanAsync("prepare a summary for each CMP RFP project");
        Assert.True(planResult.IsSuccess);
        var approved = await services.Approval.ApproveAsync(planResult.Value!.PlanId);
        Assert.True(approved.IsSuccess);
        var started = await services.Execution.StartAsync(planResult.Value.PlanId);
        Assert.True(started.IsSuccess);
        await services.RunUntilTerminalAsync(started.Value!.JobId, TimeSpan.FromSeconds(10));

        var progress = await services.Execution.GetProgressAsync(started.Value.JobId);
        Assert.True(progress.IsSuccess);
        Assert.Equal(ChatJobStatus.Succeeded, progress.Value!.Status);
        Assert.NotNull(copilot.LastOptions?.SectionOutline);
        Assert.NotEmpty(copilot.LastOptions!.SectionOutline!);
        Assert.NotNull(copilot.LastOptions?.RetrievalResults);
        Assert.Contains(copilot.LastOptions!.RetrievalResults!, result => result.Chunk.Index == 0 && result.Citations.Contains(source2022.SourceName));
        Assert.Contains(copilot.LastOptions!.RetrievalResults!, result => result.Chunk.Index == 0 && result.Citations.Contains(source2026.SourceName));
    }

    public static TestServices CreateServices(
        Func<ICopilotService>? copilotFactory = null,
        IRagsService? rags = null,
        IGraphRagService? graphRag = null,
        ILazyGraphRagService? lazyGraphRag = null,
        IKnowledgeSourceResolver? knowledgeSourceResolver = null,
        IKnowledgeSourceIngestionService? knowledgeSourceIngestion = null,
        IMetadataRepository? metadataRepository = null,
        IChatToolInvoker? toolInvoker = null,
        ChatExecutionEngineOptions? options = null,
        ChatAgentOptions? chatAgentOptions = null,
        IDocumentTemplateRegistry? templateRegistry = null,
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
        var progressStore = new InMemoryChatProgressStore();
        var telemetryService = new ChatTelemetryService();
        var invoker = toolInvoker ?? new FakeChatToolInvoker(ragsService);
        var optionsValue = options ?? new ChatExecutionEngineOptions
        {
            DefaultStepTimeoutSeconds = 30,
            MandatoryToolTimeoutSeconds = 30,
            OverallJobTimeoutSeconds = 60,
            HeartbeatIntervalSeconds = 1,
            LongWaitHeartbeatIntervalSeconds = 2,
            HeartbeatWatchdogMissedThreshold = 5,
            SmallCorpusDocumentThreshold = 5,
            SmallCorpusTimeoutSeconds = 5
        };
        var execution = new ChatExecutionEngine(
            approval,
            copilot,
            ragsService,
            graphRagService,
            lazyGraphRagService,
            globalGraphSearchService,
            invoker,
            progressStore,
            telemetryService,
            Options.Create(optionsValue),
            logger,
            chatAgentOptions is null ? null : Options.Create(chatAgentOptions),
            knowledgeSourceResolver,
            knowledgeSourceIngestion,
            metadataRepository,
            templateRegistry);
        var host = new FakeHost(execution);
        if (startWorkerLoop)
        {
            var hostTask = execution.StartAsync(CancellationToken.None);
            host.Register(hostTask);
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
        private readonly List<Task> _tasks = new();

        public FakeHost(ChatExecutionEngine engine)
        {
            _engine = engine;
        }

        public void Register(Task task)
        {
            _tasks.Add(task);
        }

        public void Dispose()
        {
            _engine.StopAsync(CancellationToken.None).Wait(TimeSpan.FromSeconds(5));
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

    private sealed class RfpScenarioCopilotService : ICopilotService
    {
        private readonly string _retrievalStrategy;

        public RfpScenarioCopilotService(string retrievalStrategy = "graphrag-global")
        {
            _retrievalStrategy = retrievalStrategy;
        }

        public Task<Result<ChatMessage>> ChatAsync(
            ChatSession session,
            string userMessage,
            ChatRequestOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ChatMessage>.Success(new ChatMessage
            {
                Role = "assistant",
                Content = "Registered RFP opportunities found: RFP Alpha [1] and RFP Beta [2].",
                Stats = new ChatCompletionStats
                {
                    EstimatedPromptTokens = 240,
                    EstimatedCompletionTokens = 60,
                    RetrievedContextCount = 2,
                    CitationCount = 2,
                    AlignmentConfidence = 0.95,
                    ConfidenceBasis = $"Heuristic based on {_retrievalStrategy} retrieval score, retrieved context count, and 2 internal citation(s)."
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

    private sealed class SessionCapturingCopilotService : ICopilotService
    {
        public ChatSession? LastSession { get; private set; }

        public Task<Result<ChatMessage>> ChatAsync(
            ChatSession session,
            string userMessage,
            ChatRequestOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastSession = session;
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

    private sealed class ContextCapturingCopilotService : ICopilotService
    {
        public ChatRequestOptions? LastOptions { get; private set; }

        public Task<Result<ChatMessage>> ChatAsync(
            ChatSession session,
            string userMessage,
            ChatRequestOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            var contextCount = options?.RetrievalResults?.Count ?? 0;
            var citationCount = options?.RetrievalResults?.Sum(result => result.Citations.Count) ?? 0;
            return Task.FromResult(Result<ChatMessage>.Success(new ChatMessage
            {
                Role = "assistant",
                Content = $"Scoped RFP summaries generated from {contextCount} context item(s).",
                Stats = new ChatCompletionStats
                {
                    EstimatedPromptTokens = 200,
                    EstimatedCompletionTokens = 50,
                    RetrievedContextCount = contextCount,
                    CitationCount = citationCount,
                    AlignmentConfidence = 0.9,
                    ConfidenceBasis = "Heuristic based on semantic retrieval score, retrieved context count, and internal citation(s)."
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

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveSourceChunksAsync(Guid sourceId, int take, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
        }
    }

    private sealed class QueryAwareRagsService : IRagsService
    {
        private readonly Func<string, IReadOnlyList<SearchResult>?> _resolve;

        public QueryAwareRagsService(Func<string, IReadOnlyList<SearchResult>?> resolve)
        {
            _resolve = resolve;
        }

        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public async Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
        {
            var results = _resolve(request.Query);
            if (results is null)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                return Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>());
            }

            return Result<IReadOnlyList<SearchResult>>.Success(results);
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveSourceChunksAsync(Guid sourceId, int take, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
        }
    }

    private sealed class HydratedSourceRagsService : IRagsService
    {
        private readonly IReadOnlyDictionary<Guid, IReadOnlyList<SearchResult>> _resultsBySource;
        private readonly HashSet<Guid> _hydratedSources = new();

        public HydratedSourceRagsService(IReadOnlyDictionary<Guid, IReadOnlyList<SearchResult>> resultsBySource)
        {
            _resultsBySource = resultsBySource;
        }

        public void MarkHydrated(Guid sourceId)
        {
            lock (_hydratedSources)
            {
                _hydratedSources.Add(sourceId);
            }
        }

        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            MarkHydrated(request.SourceId);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
        {
            if (!request.SourceId.HasValue)
            {
                return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
            }

            lock (_hydratedSources)
            {
                if (!_hydratedSources.Contains(request.SourceId.Value))
                {
                    return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
                }
            }

            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(
                _resultsBySource.TryGetValue(request.SourceId.Value, out var results)
                    ? results
                    : Array.Empty<SearchResult>()));
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveSourceChunksAsync(Guid sourceId, int take, CancellationToken cancellationToken = default)
        {
            lock (_hydratedSources)
            {
                if (!_hydratedSources.Contains(sourceId))
                {
                    return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
                }
            }

            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(
                _resultsBySource.TryGetValue(sourceId, out var results)
                    ? results
                    : Array.Empty<SearchResult>()));
        }
    }

    private sealed class SourceOnlyRagsService : IRagsService
    {
        private readonly IReadOnlyDictionary<Guid, IReadOnlyList<SearchResult>> _resultsBySource;

        public SourceOnlyRagsService(IReadOnlyDictionary<Guid, IReadOnlyList<SearchResult>> resultsBySource)
        {
            _resultsBySource = resultsBySource;
        }

        public int BroadCalls { get; private set; }

        public int SourceCalls { get; private set; }

        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
        {
            if (!request.SourceId.HasValue)
            {
                BroadCalls++;
                return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
            }

            SourceCalls++;
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(
                _resultsBySource.TryGetValue(request.SourceId.Value, out var results)
                    ? results
                    : Array.Empty<SearchResult>()));
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveSourceChunksAsync(Guid sourceId, int take, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(
                _resultsBySource.TryGetValue(sourceId, out var results)
                    ? results
                    : Array.Empty<SearchResult>()));
        }
    }

    private sealed class RecordingRagsService : IRagsService
    {
        private readonly IReadOnlyList<SearchResult> _results;

        public RecordingRagsService(IReadOnlyList<SearchResult> results)
        {
            _results = results;
        }

        public List<Guid?> Requests { get; } = new();

        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request.SourceId);
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(_results));
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveSourceChunksAsync(Guid sourceId, int take, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
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

    private sealed class FailingGraphRagService : IGraphRagService
    {
        private readonly string _error;

        public FailingGraphRagService(string error)
        {
            _error = error;
        }

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
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Failure(_error));
        }

        public Task<Result<GlobalSearchResult>> GlobalSearchAsync(string query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<GlobalSearchResult>.Failure(_error));
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

    private sealed class FailingRagsService : IRagsService
    {
        private readonly string _error;

        public FailingRagsService(string error)
        {
            _error = error;
        }

        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Failure(_error));
        }
    }

    private sealed class TrackingKnowledgeSourceIngestionService : IKnowledgeSourceIngestionService
    {
        private readonly Action<KnowledgeSource> _onIngested;
        private readonly List<KnowledgeSource> _sources = new();

        public TrackingKnowledgeSourceIngestionService(Action<KnowledgeSource> onIngested)
        {
            _onIngested = onIngested;
        }

        public IReadOnlyList<KnowledgeSource> Sources => _sources;

        public Task<Result<bool>> EnsureIngestedAsync(KnowledgeSource source, CancellationToken cancellationToken = default)
        {
            _sources.Add(source);
            _onIngested(source);
            return Task.FromResult(Result<bool>.Success(true));
        }
    }

    private sealed class FakeMetadataRepository : IMetadataRepository
    {
        private readonly IReadOnlyList<FileMetadata> _items;

        public FakeMetadataRepository(IReadOnlyList<FileMetadata> items)
        {
            _items = items;
        }

        public Task<Result<FileMetadata>> GetAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            var metadata = _items.FirstOrDefault(item => item.Descriptor.FileId == descriptor.FileId);
            return metadata is null
                ? Task.FromResult(Result<FileMetadata>.Failure("Metadata not found."))
                : Task.FromResult(Result<FileMetadata>.Success(metadata));
        }

        public Task<Result<FileMetadata>> SaveAsync(FileMetadata metadata, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<FileMetadata>.Success(metadata));
        }

        public Task<Result<PagedResult<FileMetadata>>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<PagedResult<FileMetadata>>.Success(new PagedResult<FileMetadata>(
                _items,
                request.PageNumber,
                request.PageSize,
                _items.Count)));
        }

        public Task<Result> DeleteAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
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

    private sealed class FakeChatToolInvoker : IChatToolInvoker
    {
        private readonly IRagsService _ragsService;
        private readonly string? _error;
        private readonly bool _hang;

        public FakeChatToolInvoker(IRagsService ragsService, string? error = null, bool hang = false)
        {
            _ragsService = ragsService ?? throw new ArgumentNullException(nameof(ragsService));
            _error = error;
            _hang = hang;
        }

        public int InvokeCount { get; private set; }

        public string? LastToolName { get; private set; }

        public async Task<ToolInvocationResponse> InvokeAsync(
            string toolName,
            IReadOnlyDictionary<string, string> arguments,
            CancellationToken cancellationToken = default)
        {
            InvokeCount++;
            LastToolName = toolName;

            if (_hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }

            if (_error is not null)
            {
                return new ToolInvocationResponse(_error);
            }

            var query = arguments.TryGetValue("query", out var q) ? q : string.Empty;
            var topK = arguments.TryGetValue("topK", out var tk) && int.TryParse(tk, out var topKValue)
                ? topKValue
                : 5;
            var sourceId = arguments.TryGetValue("sourceId", out var sid) && Guid.TryParse(sid, out var parsed)
                ? parsed
                : (Guid?)null;

            var result = await _ragsService.RetrieveAsync(new RetrievalRequest(query, topK, sourceId), cancellationToken).ConfigureAwait(false);
            if (result.IsFailure || result.Value is null)
            {
                return new ToolInvocationResponse(result.Error ?? "RAGS retrieval returned no results.");
            }

            return new ToolInvocationResponse(result.Value);
        }
    }

    private sealed class HangingChatToolInvoker : IChatToolInvoker
    {
        public async Task<ToolInvocationResponse> InvokeAsync(
            string toolName,
            IReadOnlyDictionary<string, string> arguments,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return new ToolInvocationResponse(Array.Empty<SearchResult>());
        }
    }

    private sealed class DelayedRagsService : IRagsService
    {
        private readonly TimeSpan _delay;
        private readonly IReadOnlyList<SearchResult> _results;

        public DelayedRagsService(TimeSpan delay, IReadOnlyList<SearchResult> results)
        {
            _delay = delay;
            _results = results;
        }

        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public async Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            return Result<IReadOnlyList<SearchResult>>.Success(_results);
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

    private sealed class AssertNeverCalledGraphRagService : IGraphRagService
    {
        public bool WasCalled { get; private set; }

        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(
            string query,
            int topK = 5,
            int maxExpanded = 10,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
        }

        public Task<Result<GlobalSearchResult>> GlobalSearchAsync(string query, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(Result<GlobalSearchResult>.Failure("GraphRAG should not be invoked in this chat path."));
        }
    }

    private sealed class AssertNeverCalledLazyGraphRagService : ILazyGraphRagService
    {
        public bool WasCalled { get; private set; }

        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(
            string query,
            int topK = 5,
            int maxExpanded = 10,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
        }

        public Task<Result<GlobalSearchResult>> GlobalSearchAsync(string query, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(Result<GlobalSearchResult>.Failure("LazyGraphRAG should not be invoked in this chat path."));
        }
    }

    private sealed class HangingKnowledgeSourceIngestionService : IKnowledgeSourceIngestionService
    {
        public int CallCount { get; private set; }

        public async Task<Result<bool>> EnsureIngestedAsync(KnowledgeSource source, CancellationToken cancellationToken = default)
        {
            CallCount++;
            await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
            return Result<bool>.Success(true);
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
            lock (_results)
            {
                LastRequest = request;
            }
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(_results));
        }
    }
}
