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
