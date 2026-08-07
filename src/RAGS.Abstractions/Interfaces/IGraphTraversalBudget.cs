using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>
/// Monitors and enforces resource budget during graph traversal.
/// All traversal decisions must honor budget constraints.
/// </summary>
public interface IGraphTraversalBudget
{
    int MaxLLMCalls { get; }
    int MaxDepth { get; }
    int MaxNodes { get; }
    int MaxRelationships { get; }
    int MaxTokenBudget { get; }
    TimeSpan MaxExecutionTime { get; }

    /// <summary>
    /// Current number of LLM calls recorded against this budget.
    /// </summary>
    int LlmCalls { get; }

    /// <summary>
    /// Current number of tokens consumed against this budget.
    /// </summary>
    int TokensConsumed { get; }

    /// <summary>
    /// Current number of node visits recorded against this budget.
    /// </summary>
    int NodesVisited { get; }

    /// <summary>
    /// Current number of relationship traversals recorded against this budget.
    /// </summary>
    int RelationshipsTraversed { get; }

    /// <summary>
    /// Creates a fresh budget with the same limits as this one. Used to give each
    /// retrieval request its own budget so concurrent requests cannot corrupt each other.
    /// </summary>
    IGraphTraversalBudget CreatePerRequest();

    /// <summary>
    /// Tracks a single LLM call. Returns false if budget exhausted.
    /// </summary>
    bool RecordLLMCall();

    /// <summary>
    /// Tracks node expansion. Returns false if budget exhausted.
    /// </summary>
    bool RecordNodeVisit();

    /// <summary>
    /// Tracks relationship traversal. Returns false if budget exhausted.
    /// </summary>
    bool RecordRelationshipTraversed();

    /// <summary>
    /// Tracks token consumption. Returns false if budget exhausted.
    /// </summary>
    bool RecordTokens(int tokenCount);

    /// <summary>
    /// Checks whether the maximum execution time has been exceeded.
    /// </summary>
    bool IsTimeExceeded();

    /// <summary>
    /// Checks whether any budget constraint has been violated.
    /// </summary>
    bool IsExceeded();

    /// <summary>
    /// Resets all counters and the timer.
    /// </summary>
    void Reset();
}
