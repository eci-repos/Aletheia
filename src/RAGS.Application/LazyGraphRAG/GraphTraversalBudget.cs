using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;

namespace Aletheia.RAGS.Application.LazyGraphRAG;

/// <summary>
/// Thread-safe graph traversal budget enforcer.
/// Tracks LLM calls, node visits, relationship traversals, token consumption, and execution time.
/// </summary>
public sealed class GraphTraversalBudget : IGraphTraversalBudget
{
    private int _llmCalls;
    private int _nodesVisited;
    private int _relationshipsTraversed;
    private int _tokensConsumed;
    private DateTime _startTime;

    public GraphTraversalBudget(
        int maxLLMCalls = 5,
        int maxDepth = 3,
        int maxNodes = 50,
        int maxRelationships = 100,
        int maxTokenBudget = 4000,
        TimeSpan? maxExecutionTime = null)
    {
        MaxLLMCalls = maxLLMCalls;
        MaxDepth = maxDepth;
        MaxNodes = maxNodes;
        MaxRelationships = maxRelationships;
        MaxTokenBudget = maxTokenBudget;
        MaxExecutionTime = maxExecutionTime ?? TimeSpan.FromSeconds(30);
        _startTime = DateTime.UtcNow;
    }

    public int MaxLLMCalls { get; }
    public int MaxDepth { get; }
    public int MaxNodes { get; }
    public int MaxRelationships { get; }
    public int MaxTokenBudget { get; }
    public TimeSpan MaxExecutionTime { get; }

    public int LlmCalls => Volatile.Read(ref _llmCalls);
    public int TokensConsumed => Volatile.Read(ref _tokensConsumed);
    public int NodesVisited => Volatile.Read(ref _nodesVisited);
    public int RelationshipsTraversed => Volatile.Read(ref _relationshipsTraversed);

    public IGraphTraversalBudget CreatePerRequest()
    {
        return new GraphTraversalBudget(MaxLLMCalls, MaxDepth, MaxNodes, MaxRelationships, MaxTokenBudget, MaxExecutionTime);
    }

    public bool RecordLLMCall()
    {
        return TryIncrementWithinLimit(ref _llmCalls, MaxLLMCalls);
    }

    public bool RecordNodeVisit()
    {
        return TryIncrementWithinLimit(ref _nodesVisited, MaxNodes);
    }

    public bool RecordRelationshipTraversed()
    {
        return TryIncrementWithinLimit(ref _relationshipsTraversed, MaxRelationships);
    }

    public bool RecordTokens(int tokenCount)
    {
        // The tokens were genuinely consumed by the LLM, so always record them to
        // keep accounting honest. Return whether the new total is still within the
        // budget so callers and IsExceeded() can halt the traversal on a breach.
        while (true)
        {
            var current = Volatile.Read(ref _tokensConsumed);
            var updated = current + tokenCount;
            if (Interlocked.CompareExchange(ref _tokensConsumed, updated, current) == current)
            {
                return updated <= MaxTokenBudget;
            }
        }
    }

    public bool IsTimeExceeded()
    {
        return DateTime.UtcNow - _startTime > MaxExecutionTime;
    }

    public bool IsExceeded()
    {
        return _llmCalls > MaxLLMCalls
            || _nodesVisited > MaxNodes
            || _relationshipsTraversed > MaxRelationships
            || _tokensConsumed > MaxTokenBudget
            || IsTimeExceeded();
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _llmCalls, 0);
        Interlocked.Exchange(ref _nodesVisited, 0);
        Interlocked.Exchange(ref _relationshipsTraversed, 0);
        Interlocked.Exchange(ref _tokensConsumed, 0);
        _startTime = DateTime.UtcNow;
    }

    private static bool TryIncrementWithinLimit(ref int counter, int limit)
    {
        while (true)
        {
            var current = Volatile.Read(ref counter);
            if (current >= limit)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref counter, current + 1, current) == current)
            {
                return true;
            }
        }
    }
}
