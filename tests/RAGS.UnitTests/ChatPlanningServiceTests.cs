using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.Planning;
using Microsoft.Extensions.Options;

namespace RAGS.UnitTests;

    public class ChatPlanningServiceTests
    {
        [Theory]
        [InlineData("Provide a summary of RFP's as registered in the last 10 years", ChatExecutionMode.TimelineAnalysis, true, true)]
        [InlineData("What are the RFP requirements for CMP?", ChatExecutionMode.CorpusAnalysis, true, true)]
        [InlineData("Show me WRAGS wiki pages about procurement", ChatExecutionMode.CorpusAnalysis, true, true)]
        [InlineData("what does CMP say?", ChatExecutionMode.Retrieval, false, false)]
        public async Task Rfp_and_repository_queries_route_to_retrieval_modes(string prompt, ChatExecutionMode expectedMode, bool requiresApproval, bool requiresToolCall)
        {
            var service = new ChatPlanningService();

            var result = await service.CreatePlanAsync(prompt);

            Assert.True(result.IsSuccess);
            Assert.Equal(expectedMode, result.Value!.Mode);
            Assert.Equal(requiresApproval, result.Value.RequiresApproval);
            Assert.Equal(requiresToolCall, result.Value.RequiresToolCall);
            if (requiresToolCall)
            {
                Assert.NotEmpty(result.Value.ToolName);
                Assert.Contains(result.Value.Steps, s => s.StartsWith("Call repository tool", StringComparison.OrdinalIgnoreCase));
            }
            Assert.NotEqual(0, result.Value.EstimatedRetrievalCount);
        }

        [Fact]
    public async Task CreatePlanAsync_emits_tool_call_for_rfp_timeline_query()
        {
            var service = new ChatPlanningService();

            var result = await service.CreatePlanAsync("Summarize registered RFP opportunities in the past 10 years");

            Assert.True(result.IsSuccess);
            var plan = result.Value!;
            Assert.True(plan.RequiresToolCall);
            Assert.Equal("AletheiaKnowledgePlugin.SearchRags", plan.ToolName);
            Assert.Contains("query", plan.ToolArguments.Keys);
            Assert.Contains("topK", plan.ToolArguments.Keys);
            Assert.Contains(plan.Steps, s => s.StartsWith("Call repository tool", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePlanAsync_routes_broad_non_rfp_corpus_query_to_graphrag()
    {
        var service = new ChatPlanningService();

        var result = await service.CreatePlanAsync("summarize the corpus");

        Assert.True(result.IsSuccess);
        var plan = result.Value!;
        Assert.True(plan.RequiresToolCall);
        Assert.Equal("AletheiaKnowledgePlugin.SearchGraphRag", plan.ToolName);
    }


    [Theory]
    [InlineData("hello", ChatExecutionMode.FastPath)]
    [InlineData("what is the capital of France?", ChatExecutionMode.Retrieval)]
    [InlineData("what does CMP say about activities?", ChatExecutionMode.Retrieval)]
    [InlineData("summarize the corpus", ChatExecutionMode.CorpusAnalysis)]
    [InlineData("identify every RFP requirement", ChatExecutionMode.CorpusAnalysis)]
    [InlineData("compare the two proposals", ChatExecutionMode.ComparativeAnalysis)]
    [InlineData("build a timeline of changes over the last 5 years", ChatExecutionMode.TimelineAnalysis)]
    [InlineData("create a matrix of requirements", ChatExecutionMode.StructuredSynthesis)]
    public async Task AnalyzePromptAsync_classifies_prompts_into_expected_modes(string prompt, ChatExecutionMode expectedMode)
    {
        var service = new ChatPlanningService();

        var result = await service.AnalyzePromptAsync(prompt);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedMode, result.Value!.SuggestedMode);
    }

    [Theory]
    [InlineData("all documents")]
    [InlineData("all RFPs")]
    [InlineData("summarize corpus")]
    [InlineData("identify every requirement")]
    public async Task AnalyzePromptAsync_detects_broad_corpus_requests(string prompt)
    {
        var service = new ChatPlanningService();

        var result = await service.AnalyzePromptAsync(prompt);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsBroadCorpusRequest);
        Assert.True(result.Value.RequiresApproval);
    }

    [Fact]
    public async Task CreatePlanAsync_treats_list_all_found_features_as_exhaustive_scoped_request()
    {
        var service = new ChatPlanningService();

        var result = await service.CreatePlanAsync("on the CMP 2026 list all found features required for AI");

        Assert.True(result.IsSuccess);
        var plan = result.Value!;
        Assert.Equal(ChatExecutionMode.CorpusAnalysis, plan.Mode);
        Assert.True(plan.RequiresApproval);
        Assert.True(plan.EstimatedRetrievalCount > 0);
        Assert.True(plan.RequiresToolCall);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", plan.ToolName);
    }

    [Fact]
    public async Task CreatePlanAsync_routes_cmp_engagement_feature_request_to_document_rags()
    {
        var service = new ChatPlanningService();

        var result = await service.CreatePlanAsync("Base on CMP 2026 list required features for this engagement");

        Assert.True(result.IsSuccess);
        var plan = result.Value!;
        Assert.Equal(ChatExecutionMode.CorpusAnalysis, plan.Mode);
        Assert.True(plan.RequiresApproval);
        Assert.True(plan.RequiresToolCall);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", plan.ToolName);
        Assert.Contains("query", plan.ToolArguments.Keys);
        Assert.Contains("topK", plan.ToolArguments.Keys);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("hi")]
    [InlineData("explain this word")]
    public async Task AnalyzePromptAsync_keeps_simple_prompts_fast_path(string prompt)
    {
        var service = new ChatPlanningService();

        var result = await service.AnalyzePromptAsync(prompt);

        Assert.True(result.IsSuccess);
        Assert.Equal(ChatExecutionMode.FastPath, result.Value!.SuggestedMode);
        Assert.False(result.Value.IsBroadCorpusRequest);
        Assert.False(result.Value.RequiresApproval);
    }

    [Theory]
    [InlineData("what does the CMP RFP require?")]
    [InlineData("list requirements from the RFP")]
    public async Task CreatePlanAsync_marks_rfp_queries_as_tool_call_with_verification_step(string prompt)
    {
        var service = new ChatPlanningService();

        var result = await service.CreatePlanAsync(prompt);

        Assert.True(result.IsSuccess);
        var plan = result.Value!;
        Assert.True(plan.RequiresToolCall);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", plan.ToolName);
        Assert.Contains(plan.Steps, s => s.StartsWith("Call repository tool", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Steps, s => s.Contains("Verify tool returned internal context before synthesis", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePlanAsync_produces_steps_for_corpus_analysis()
    {
        var service = new ChatPlanningService();

        var result = await service.CreatePlanAsync("summarize all RFPs in the repository");

        Assert.True(result.IsSuccess);
        var plan = result.Value!;
        Assert.Equal(ChatExecutionMode.CorpusAnalysis, plan.Mode);
        Assert.Contains("Classify user intent", plan.Steps);
        Assert.Contains(plan.Steps, s => s.StartsWith("Call repository tool", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Synthesize answer with citations", plan.Steps);
        Assert.Contains("Return final response", plan.Steps);
        Assert.True(plan.EstimatedSecondsMin > 0);
        Assert.True(plan.EstimatedSecondsMax >= plan.EstimatedSecondsMin);
        Assert.True(plan.EstimatedLlmCalls > 1);
        Assert.True(plan.EstimatedRetrievalCount > 10);
        Assert.True(plan.RequiresApproval);
        Assert.True(plan.RequiresToolCall);
        Assert.Equal("AletheiaKnowledgePlugin.SearchRags", plan.ToolName);
    }

    [Fact]
    public async Task CreatePlanAsync_fast_path_has_no_retrieval_and_no_approval()
    {
        var service = new ChatPlanningService();

        var result = await service.CreatePlanAsync("hello");

        Assert.True(result.IsSuccess);
        var plan = result.Value!;
        Assert.Equal(ChatExecutionMode.FastPath, plan.Mode);
        Assert.Equal(0, plan.EstimatedRetrievalCount);
        Assert.Equal(1, plan.EstimatedLlmCalls);
        Assert.False(plan.RequiresApproval);
        Assert.True(plan.ExpiresAt > plan.CreatedAt);
    }

    [Fact]
    public async Task CreatePlanAsync_uses_supplied_analysis_when_provided()
    {
        var service = new ChatPlanningService();
        var analysis = new PromptAnalysis
        {
            SuggestedMode = ChatExecutionMode.ComparativeAnalysis,
            EstimatedPromptTokens = 100,
            IsBroadCorpusRequest = false,
            RequiresApproval = false
        };

        var result = await service.CreatePlanAsync("x", analysis);

        Assert.True(result.IsSuccess);
        Assert.Equal(ChatExecutionMode.ComparativeAnalysis, result.Value!.Mode);
    }

    [Fact]
    public void RequiresApproval_returns_true_for_high_cost_plan()
    {
        var options = Options.Create(new ChatPlanningOptions { ApprovalThresholdSeconds = 10 });
        var service = new ChatPlanningService(options);
        var plan = new ChatExecutionPlan
        {
            Mode = ChatExecutionMode.Retrieval,
            EstimatedSecondsMax = 20,
            EstimatedLlmCalls = 1,
            EstimatedRetrievalCount = 5
        };

        Assert.True(service.RequiresApproval(plan));
    }

    [Fact]
    public void RequiresApproval_returns_false_for_fast_path_plan()
    {
        var options = Options.Create(new ChatPlanningOptions { ApprovalThresholdSeconds = 10 });
        var service = new ChatPlanningService(options);
        var plan = new ChatExecutionPlan
        {
            Mode = ChatExecutionMode.FastPath,
            EstimatedSecondsMax = 2,
            EstimatedLlmCalls = 1,
            EstimatedRetrievalCount = 0
        };

        Assert.False(service.RequiresApproval(plan));
    }

    [Fact]
    public void RequiresApproval_returns_true_when_llm_call_threshold_met()
    {
        var options = Options.Create(new ChatPlanningOptions { ApprovalThresholdLlmCalls = 3 });
        var service = new ChatPlanningService(options);
        var plan = new ChatExecutionPlan
        {
            Mode = ChatExecutionMode.Retrieval,
            EstimatedSecondsMax = 5,
            EstimatedLlmCalls = 3,
            EstimatedRetrievalCount = 5
        };

        Assert.True(service.RequiresApproval(plan));
    }

    [Fact]
    public void RequiresApproval_returns_true_when_retrieval_threshold_met()
    {
        var options = Options.Create(new ChatPlanningOptions { ApprovalThresholdRetrievalCount = 20 });
        var service = new ChatPlanningService(options);
        var plan = new ChatExecutionPlan
        {
            Mode = ChatExecutionMode.Retrieval,
            EstimatedSecondsMax = 5,
            EstimatedLlmCalls = 1,
            EstimatedRetrievalCount = 25
        };

        Assert.True(service.RequiresApproval(plan));
    }

    [Fact]
    public async Task EstimatePlanAsync_updates_requires_approval_flag()
    {
        var service = new ChatPlanningService();
        var initial = new ChatExecutionPlan
        {
            Mode = ChatExecutionMode.CorpusAnalysis,
            EstimatedSecondsMax = 120,
            EstimatedLlmCalls = 3,
            EstimatedRetrievalCount = 50,
            RequiresApproval = false
        };

        var result = await service.EstimatePlanAsync(initial);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.RequiresApproval);
    }

    [Fact]
    public async Task AnalyzePromptAsync_populates_token_estimate_and_signals()
    {
        var service = new ChatPlanningService();

        var result = await service.AnalyzePromptAsync("what happened in the last 3 years across all RFPs?");

        Assert.True(result.IsSuccess);
        var analysis = result.Value!;
        Assert.True(analysis.EstimatedPromptTokens > 0);
        Assert.Contains("temporal", analysis.DetectedIntentSignals);
        Assert.Contains("corpus-wide", analysis.DetectedIntentSignals);
        Assert.True(analysis.Confidence > 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnalyzePromptAsync_rejects_empty_prompt(string? prompt)
    {
        var service = new ChatPlanningService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.AnalyzePromptAsync(prompt!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreatePlanAsync_rejects_empty_prompt(string? prompt)
    {
        var service = new ChatPlanningService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreatePlanAsync(prompt!));
    }

    [Fact]
    public async Task EstimatePlanAsync_rejects_null_plan()
    {
        var service = new ChatPlanningService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.EstimatePlanAsync(null!));
    }

    [Fact]
    public void RequiresApproval_rejects_null_plan()
    {
        var service = new ChatPlanningService();

        Assert.Throws<ArgumentNullException>(() => service.RequiresApproval(null!));
    }
}
