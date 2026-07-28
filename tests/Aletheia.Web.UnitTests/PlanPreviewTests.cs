using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Web.Pages.Copilot;
using Bunit;

namespace Aletheia.Web.UnitTests;

public class PlanPreviewTests : TestContext
{
    [Fact]
    public void Renders_plan_details_and_steps()
    {
        var expiresAt = new DateTimeOffset(2026, 7, 26, 19, 0, 0, TimeSpan.Zero);
        var plan = new ChatPlanRecord
        {
            PlanId = Guid.NewGuid(),
            Prompt = "summarize corpus",
            Mode = ChatExecutionMode.CorpusAnalysis,
            Steps = new[] { "Classify intent", "Retrieve context", "Synthesize" },
            EstimatedSecondsMin = 10,
            EstimatedSecondsMax = 30,
            EstimatedLlmCalls = 2,
            EstimatedRetrievalCount = 50,
            RequiresApproval = true,
            ExpiresAt = expiresAt
        };

        var cut = RenderComponent<PlanPreview>(parameters => parameters
            .Add(p => p.Plan, plan)
            .Add(p => p.IsBusy, false));

        Assert.Contains("Execution Plan", cut.Markup);
        Assert.Contains("Approval required", cut.Markup);
        Assert.Contains("CorpusAnalysis", cut.Markup);
        Assert.Contains("10 - 30 seconds", cut.Markup);
        Assert.Contains("Estimated model calls", cut.Markup);
        Assert.Contains("Classify intent", cut.Markup);
        Assert.Contains("Retrieve context", cut.Markup);
        Assert.Contains("Synthesize", cut.Markup);
        Assert.Contains("btn btn-success", cut.Markup);
        Assert.Contains("Revise", cut.Markup);
        Assert.Contains("Cancel", cut.Markup);
        Assert.Contains(expiresAt.ToString("g"), cut.Markup);
    }

    [Fact]
    public void Does_not_render_when_plan_is_null()
    {
        var cut = RenderComponent<PlanPreview>();

        Assert.Empty(cut.Markup);
    }

    [Fact]
    public void Fast_path_shows_success_badge_and_only_run_revise()
    {
        var plan = new ChatPlanRecord
        {
            PlanId = Guid.NewGuid(),
            Prompt = "hello",
            Mode = ChatExecutionMode.FastPath,
            Steps = new[] { "Answer directly" },
            EstimatedSecondsMin = 1,
            EstimatedSecondsMax = 5,
            EstimatedLlmCalls = 1,
            EstimatedRetrievalCount = 0,
            RequiresApproval = false
        };

        var cut = RenderComponent<PlanPreview>(parameters => parameters.Add(p => p.Plan, plan));

        Assert.Contains("Fast path", cut.Markup);
        Assert.Contains("btn btn-primary", cut.Markup);
        Assert.DoesNotContain("btn btn-success", cut.Markup);
        Assert.DoesNotContain("Cancel", cut.Markup);
    }
}
