using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.Planning;

namespace RAGS.UnitTests;

public class ChatProgressStoreTests
{
    [Fact]
    public async Task SaveAsync_and_GetAsync_round_trip_progress_record()
    {
        var store = new InMemoryChatProgressStore();
        var record = CreateRecord();

        var save = await store.SaveAsync(record);
        var get = await store.GetAsync(record.JobId);

        Assert.True(save.IsSuccess);
        Assert.True(get.IsSuccess);
        Assert.NotNull(get.Value);
        Assert.Equal(record.JobId, get.Value!.JobId);
        Assert.Equal(record.PlanId, get.Value.PlanId);
        Assert.Equal("prompt", get.Value.Prompt);
        Assert.Equal(ChatJobStatus.Queued, get.Value.Status);
        Assert.Equal(10, get.Value.Steps.Count);
    }

    [Fact]
    public async Task UpdateStepAsync_updates_existing_step_status()
    {
        var store = new InMemoryChatProgressStore();
        var record = CreateRecord();
        await store.SaveAsync(record);

        var update = await store.UpdateStepAsync(record.JobId, new ChatProgressStep
        {
            Name = "Retrieving context",
            Status = ChatProgressStepStatus.Running,
            Order = 3,
            StartedAt = DateTimeOffset.UtcNow,
            Detail = "Retrieving chunks."
        });

        Assert.True(update.IsSuccess);
        var progress = (await store.GetAsync(record.JobId)).Value!;
        var step = progress.Steps.First(s => s.Name == "Retrieving context");
        Assert.Equal(ChatProgressStepStatus.Running, step.Status);
        Assert.Equal("Retrieving chunks.", step.Detail);
    }

    [Fact]
    public async Task AppendHeartbeatAsync_adds_heartbeat_to_record()
    {
        var store = new InMemoryChatProgressStore();
        var record = CreateRecord();
        await store.SaveAsync(record);

        var heartbeat = new ChatProgressHeartbeat
        {
            Stage = "Synthesis",
            Detail = "Still generating.",
            PercentComplete = 80
        };
        var append = await store.AppendHeartbeatAsync(record.JobId, heartbeat);

        Assert.True(append.IsSuccess);
        var progress = (await store.GetAsync(record.JobId)).Value!;
        Assert.Single(progress.Heartbeats);
        Assert.Equal("Synthesis", progress.Heartbeats[0].Stage);
        Assert.Equal(80, progress.Heartbeats[0].PercentComplete);
    }

    [Fact]
    public async Task AppendMessageAsync_adds_message_to_record()
    {
        var store = new InMemoryChatProgressStore();
        var record = CreateRecord();
        await store.SaveAsync(record);

        var append = await store.AppendMessageAsync(record.JobId, new ChatProgressMessage
        {
            Stage = "Global search",
            Message = "Fallback to RAGS."
        });

        Assert.True(append.IsSuccess);
        var progress = (await store.GetAsync(record.JobId)).Value!;
        Assert.Single(progress.Messages);
        Assert.Equal("Fallback to RAGS.", progress.Messages[0].Message);
    }

    [Fact]
    public async Task SetPartialResultAsync_persists_partial_result()
    {
        var store = new InMemoryChatProgressStore();
        var record = CreateRecord();
        await store.SaveAsync(record);

        var update = await store.SetPartialResultAsync(record.JobId, "Retrieved 5 chunks.");

        Assert.True(update.IsSuccess);
        var progress = (await store.GetAsync(record.JobId)).Value!;
        Assert.Equal("Retrieved 5 chunks.", progress.PartialResult);
    }

    [Fact]
    public async Task FinalizeAsync_sets_status_result_and_error()
    {
        var store = new InMemoryChatProgressStore();
        var record = CreateRecord();
        await store.SaveAsync(record);

        var finalize = await store.FinalizeAsync(record.JobId, ChatJobStatus.Succeeded, "Final answer.", null);

        Assert.True(finalize.IsSuccess);
        var progress = (await store.GetAsync(record.JobId)).Value!;
        Assert.Equal(ChatJobStatus.Succeeded, progress.Status);
        Assert.Equal("Final answer.", progress.FinalResult);
        Assert.Null(progress.Error);
        Assert.NotNull(progress.CompletedAt);
    }

    [Fact]
    public async Task FinalizeAsync_success_does_not_overwrite_completed_steps_as_failed()
    {
        var store = new InMemoryChatProgressStore();
        var record = CreateRecord();
        await store.SaveAsync(record);
        await store.UpdateStepAsync(record.JobId, new ChatProgressStep
        {
            Name = "Planning",
            Status = ChatProgressStepStatus.Running,
            Order = 0,
            StartedAt = DateTimeOffset.UtcNow
        });
        await store.UpdateStepAsync(record.JobId, new ChatProgressStep
        {
            Name = "Completed",
            Status = ChatProgressStepStatus.Completed,
            Order = 9,
            CompletedAt = DateTimeOffset.UtcNow
        });

        var finalize = await store.FinalizeAsync(record.JobId, ChatJobStatus.Succeeded, "Final answer.", null);

        Assert.True(finalize.IsSuccess);
        var progress = (await store.GetAsync(record.JobId)).Value!;
        Assert.Equal(ChatJobStatus.Succeeded, progress.Status);
        Assert.Contains(progress.Steps, s => s.Name == "Completed" && s.Status == ChatProgressStepStatus.Completed);
        var planning = progress.Steps.First(s => s.Name == "Planning");
        Assert.Equal(ChatProgressStepStatus.Running, planning.Status);
    }

    [Fact]
    public async Task FinalizeAsync_failed_marks_running_steps_as_failed()
    {
        var store = new InMemoryChatProgressStore();
        var record = CreateRecord();
        await store.SaveAsync(record);
        await store.UpdateStepAsync(record.JobId, new ChatProgressStep
        {
            Name = "Retrieving context",
            Status = ChatProgressStepStatus.Running,
            Order = 3,
            StartedAt = DateTimeOffset.UtcNow
        });

        var finalize = await store.FinalizeAsync(record.JobId, ChatJobStatus.Failed, null, "Retrieval timed out.");

        Assert.True(finalize.IsSuccess);
        var progress = (await store.GetAsync(record.JobId)).Value!;
        Assert.Equal(ChatJobStatus.Failed, progress.Status);
        var step = progress.Steps.First(s => s.Name == "Retrieving context");
        Assert.Equal(ChatProgressStepStatus.Failed, step.Status);
        Assert.Equal("Retrieval timed out.", step.Detail);
    }

    [Fact]
    public async Task UpdateStepAsync_fails_when_record_missing()
    {
        var store = new InMemoryChatProgressStore();

        var result = await store.UpdateStepAsync(Guid.NewGuid(), new ChatProgressStep { Name = "x" });

        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Steps_are_sorted_by_order()
    {
        var store = new InMemoryChatProgressStore();
        var record = CreateRecord();
        await store.SaveAsync(record);

        await store.UpdateStepAsync(record.JobId, new ChatProgressStep
        {
            Name = "Completed",
            Status = ChatProgressStepStatus.Completed,
            Order = 9
        });
        await store.UpdateStepAsync(record.JobId, new ChatProgressStep
        {
            Name = "Planning",
            Status = ChatProgressStepStatus.Completed,
            Order = 0
        });

        var progress = (await store.GetAsync(record.JobId)).Value!;
        Assert.Equal("Planning", progress.Steps[0].Name);
        Assert.Equal("Completed", progress.Steps[^1].Name);
    }

    [Fact]
    public async Task Engine_persists_steps_and_progress_during_execution()
    {
        var services = ChatExecutionEngineTests.CreateServices();
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
        Assert.True(progress.Value!.Steps.Count >= 5);
        Assert.Contains(progress.Value.Steps, s => s.Status == ChatProgressStepStatus.Completed);
    }

    private static ChatProgressRecord CreateRecord()
    {
        return new ChatProgressRecord
        {
            JobId = Guid.NewGuid(),
            PlanId = Guid.NewGuid(),
            Prompt = "prompt",
            Status = ChatJobStatus.Queued,
            Steps = new List<ChatProgressStep>
            {
                new() { Name = "Planning", Status = ChatProgressStepStatus.Pending, Order = 0 },
                new() { Name = "Finding candidate sources", Status = ChatProgressStepStatus.Pending, Order = 1 },
                new() { Name = "Filtering sources", Status = ChatProgressStepStatus.Pending, Order = 2 },
                new() { Name = "Retrieving context", Status = ChatProgressStepStatus.Pending, Order = 3 },
                new() { Name = "Expanding graph context", Status = ChatProgressStepStatus.Pending, Order = 4 },
                new() { Name = "Extracting requested facts", Status = ChatProgressStepStatus.Pending, Order = 5 },
                new() { Name = "Validating citations", Status = ChatProgressStepStatus.Pending, Order = 6 },
                new() { Name = "Synthesizing answer", Status = ChatProgressStepStatus.Pending, Order = 7 },
                new() { Name = "Finalizing telemetry", Status = ChatProgressStepStatus.Pending, Order = 8 },
                new() { Name = "Completed", Status = ChatProgressStepStatus.Pending, Order = 9 }
            }
        };
    }
}
