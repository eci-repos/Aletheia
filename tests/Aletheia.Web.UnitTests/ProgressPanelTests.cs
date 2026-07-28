using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Web.Pages.Copilot;
using Bunit;

namespace Aletheia.Web.UnitTests;

public class ProgressPanelTests : TestContext
{
    [Fact]
    public void Renders_progress_bar_and_steps()
    {
        var progress = new ChatProgressRecord
        {
            JobId = Guid.NewGuid(),
            PlanId = Guid.NewGuid(),
            Prompt = "summarize corpus",
            Status = ChatJobStatus.Running,
            PercentComplete = 45,
            Steps = new List<ChatProgressStep>
            {
                new() { Name = "Planning", Status = ChatProgressStepStatus.Completed, Order = 0 },
                new() { Name = "Retrieving context", Status = ChatProgressStepStatus.Running, Order = 3, Detail = "Retrieving chunks" },
                new() { Name = "Synthesizing answer", Status = ChatProgressStepStatus.Pending, Order = 7 }
            },
            PartialResult = "Retrieved 12 chunks"
        };

        var cut = RenderComponent<ProgressPanel>(parameters => parameters.Add(p => p.Progress, progress));

        Assert.Contains("Background Execution", cut.Markup);
        Assert.Contains("Running", cut.Markup);
        Assert.Contains("45%", cut.Markup);
        Assert.Contains("Retrieved 12 chunks", cut.Markup);
        Assert.Contains("Retrieving chunks", cut.Markup);
        Assert.Contains("Cancel execution", cut.Markup);
    }

    [Fact]
    public void Succeeded_state_shows_completed_message_and_no_cancel_button()
    {
        var progress = new ChatProgressRecord
        {
            JobId = Guid.NewGuid(),
            PlanId = Guid.NewGuid(),
            Status = ChatJobStatus.Succeeded,
            PercentComplete = 100,
            FinalResult = "Final answer text",
            Steps = new List<ChatProgressStep>
            {
                new() { Name = "Completed", Status = ChatProgressStepStatus.Completed, Order = 9 }
            },
            CompletedAt = DateTimeOffset.UtcNow
        };

        var cut = RenderComponent<ProgressPanel>(parameters => parameters.Add(p => p.Progress, progress));

        Assert.Contains("Execution completed successfully", cut.Markup);
        Assert.Contains("bg-success", cut.Markup);
        Assert.DoesNotContain("Cancel execution", cut.Markup);
    }

    [Fact]
    public void Failed_state_shows_error_and_no_success_message()
    {
        var progress = new ChatProgressRecord
        {
            JobId = Guid.NewGuid(),
            Status = ChatJobStatus.Failed,
            PercentComplete = 50,
            Error = "Something went wrong",
            Steps = new List<ChatProgressStep>
            {
                new() { Name = "Retrieving context", Status = ChatProgressStepStatus.Failed, Order = 3 }
            }
        };

        var cut = RenderComponent<ProgressPanel>(parameters => parameters.Add(p => p.Progress, progress));

        Assert.Contains("Something went wrong", cut.Markup);
        Assert.Contains("Execution failed", cut.Markup);
        Assert.DoesNotContain("Execution completed successfully", cut.Markup);
        Assert.DoesNotContain("Cancel execution", cut.Markup);
    }

    [Fact]
    public void Completed_step_renders_success_badge()
    {
        var progress = new ChatProgressRecord
        {
            JobId = Guid.NewGuid(),
            Status = ChatJobStatus.Succeeded,
            PercentComplete = 100,
            Steps = new List<ChatProgressStep>
            {
                new() { Name = "Planning", Status = ChatProgressStepStatus.Completed, Order = 0 },
                new() { Name = "Retrieving context", Status = ChatProgressStepStatus.Completed, Order = 3 },
                new() { Name = "Synthesizing answer", Status = ChatProgressStepStatus.Completed, Order = 7 },
                new() { Name = "Completed", Status = ChatProgressStepStatus.Completed, Order = 9 }
            },
            CompletedAt = DateTimeOffset.UtcNow
        };

        var cut = RenderComponent<ProgressPanel>(parameters => parameters.Add(p => p.Progress, progress));

        var completedBadges = cut.FindAll(".bg-success");
        Assert.True(completedBadges.Count >= 4, $"Expected at least 4 success badges, found {completedBadges.Count}");
    }

    [Fact]
    public void Failed_state_shows_error_banner()
    {
        var progress = new ChatProgressRecord
        {
            JobId = Guid.NewGuid(),
            Status = ChatJobStatus.Failed,
            PercentComplete = 50,
            Error = "Something went wrong"
        };

        var cut = RenderComponent<ProgressPanel>(parameters => parameters.Add(p => p.Progress, progress));

        Assert.Contains("Something went wrong", cut.Markup);
        Assert.Contains("Execution failed", cut.Markup);
        Assert.DoesNotContain("Cancel execution", cut.Markup);
    }

    [Fact]
    public void Renders_telemetry_section_when_telemetry_provided()
    {
        var progress = new ChatProgressRecord
        {
            JobId = Guid.NewGuid(),
            Status = ChatJobStatus.Succeeded,
            PercentComplete = 100,
            FinalResult = "Answer",
            Telemetry = new ChatExecutionTelemetry
            {
                ElapsedSeconds = 3.5,
                PromptTokens = 120,
                CompletionTokens = 80,
                TokensPerSecond = 22.85,
                RetrievalCount = 12,
                CitationCount = 4,
                LlmCallCount = 1,
                EstimatedSecondsMin = 1,
                EstimatedSecondsMax = 5,
                EstimatedRetrievalCount = 10,
                EstimatedLlmCalls = 1,
                AlignmentConfidence = 0.92,
                ConfidenceBasis = "Provider-reported metrics.",
                EstimateComparisonSummary = "Duration: actual 3.5s within estimate 1-5s."
            }
        };

        var cut = RenderComponent<ProgressPanel>(parameters => parameters
            .Add(p => p.Progress, progress)
            .Add(p => p.Telemetry, progress.Telemetry));

        Assert.Contains("Execution Telemetry", cut.Markup);
        Assert.Contains("3.5s", cut.Markup);
        Assert.Contains("120", cut.Markup);
        Assert.Contains("12 chunks", cut.Markup);
        Assert.Contains("Provider-reported metrics.", cut.Markup);
        Assert.Contains("Duration: actual 3.5s within estimate 1-5s.", cut.Markup);
    }

    [Fact]
    public void Does_not_render_when_progress_is_null()
    {
        var cut = RenderComponent<ProgressPanel>();

        Assert.Empty(cut.Markup);
    }
}