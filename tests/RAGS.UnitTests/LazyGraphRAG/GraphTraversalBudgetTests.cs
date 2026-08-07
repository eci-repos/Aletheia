using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Application.LazyGraphRAG;

namespace RAGS.UnitTests.LazyGraphRAG;

public sealed class GraphTraversalBudgetTests
{
    [Fact]
    public void CreatePerRequest_returns_a_fresh_budget_with_the_same_limits()
    {
        var template = new GraphTraversalBudget(
            maxLLMCalls: 7,
            maxDepth: 4,
            maxNodes: 60,
            maxRelationships: 120,
            maxTokenBudget: 8000,
            maxExecutionTime: TimeSpan.FromSeconds(45));

        var perRequest = template.CreatePerRequest();

        Assert.NotNull(perRequest);
        Assert.NotSame(template, perRequest);
        Assert.Equal(template.MaxLLMCalls, perRequest.MaxLLMCalls);
        Assert.Equal(template.MaxDepth, perRequest.MaxDepth);
        Assert.Equal(template.MaxNodes, perRequest.MaxNodes);
        Assert.Equal(template.MaxRelationships, perRequest.MaxRelationships);
        Assert.Equal(template.MaxTokenBudget, perRequest.MaxTokenBudget);
        Assert.Equal(template.MaxExecutionTime, perRequest.MaxExecutionTime);
    }

    [Fact]
    public void CreatePerRequest_instances_do_not_share_counters()
    {
        var template = new GraphTraversalBudget();
        var first = template.CreatePerRequest();
        var second = template.CreatePerRequest();

        first.RecordLLMCall();
        first.RecordNodeVisit();
        first.RecordTokens(100);

        Assert.Equal(1, first.LlmCalls);
        Assert.Equal(1, first.NodesVisited);
        Assert.Equal(100, first.TokensConsumed);

        Assert.Equal(0, second.LlmCalls);
        Assert.Equal(0, second.NodesVisited);
        Assert.Equal(0, second.TokensConsumed);

        // The template is untouched too.
        Assert.Equal(0, template.LlmCalls);
        Assert.Equal(0, template.TokensConsumed);
    }

    [Fact]
    public void RecordTokens_returns_false_when_the_budget_would_be_exceeded()
    {
        var budget = new GraphTraversalBudget(maxTokenBudget: 100);

        Assert.True(budget.RecordTokens(60));
        Assert.False(budget.RecordTokens(60)); // 60 + 60 > 100
        Assert.Equal(120, budget.TokensConsumed); // tokens were genuinely consumed and stay recorded
        Assert.True(budget.IsExceeded());
    }

    [Fact]
    public void RecordLLMCall_returns_false_when_the_limit_is_reached()
    {
        var budget = new GraphTraversalBudget(maxLLMCalls: 2);

        Assert.True(budget.RecordLLMCall());
        Assert.True(budget.RecordLLMCall());
        Assert.False(budget.RecordLLMCall());
        Assert.Equal(2, budget.LlmCalls);
    }

    [Fact]
    public void IsExceeded_returns_true_when_execution_time_passes()
    {
        var budget = new GraphTraversalBudget(maxExecutionTime: TimeSpan.Zero);

        Assert.True(budget.IsTimeExceeded());
        Assert.True(budget.IsExceeded());
    }

    [Fact]
    public void Counter_properties_reflect_recorded_activity()
    {
        IGraphTraversalBudget budget = new GraphTraversalBudget();

        budget.RecordLLMCall();
        budget.RecordNodeVisit();
        budget.RecordNodeVisit();
        budget.RecordRelationshipTraversed();
        budget.RecordTokens(42);

        Assert.Equal(1, budget.LlmCalls);
        Assert.Equal(2, budget.NodesVisited);
        Assert.Equal(1, budget.RelationshipsTraversed);
        Assert.Equal(42, budget.TokensConsumed);
    }
}
