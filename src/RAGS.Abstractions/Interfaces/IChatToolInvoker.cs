using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>
/// Invokes a repository tool by name for the chat execution engine.
/// Implementations are responsible for dispatching to the registered plugin/function
/// (e.g. Semantic Kernel) and returning normalized search results.
/// </summary>
public interface IChatToolInvoker
{
    /// <summary>
    /// Invokes the named repository tool with the supplied arguments.
    /// </summary>
    /// <param name="toolName">Fully qualified tool name such as "AletheiaKnowledgePlugin.SearchRags" or "RepositoryTool.SearchRepositoryDocuments".</param>
    /// <param name="arguments">Arguments produced by the planner (e.g. query, topK).</param>
    /// <param name="cancellationToken">Cancellation token bound to the step timeout.</param>
    /// <returns>Search results from the tool, or an error explaining why the tool could not return results.</returns>
    Task<ToolInvocationResponse> InvokeAsync(
        string toolName,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a chat tool invocation.
/// </summary>
public sealed class ToolInvocationResponse
{
    public ToolInvocationResponse(IReadOnlyList<SearchResult> results, int invocationCount = 1)
    {
        Results = results ?? throw new ArgumentNullException(nameof(results));
        InvocationCount = invocationCount;
    }

    public ToolInvocationResponse(string error, int invocationCount = 0)
    {
        Error = error ?? "Tool invocation failed.";
        Results = Array.Empty<SearchResult>();
        InvocationCount = invocationCount;
    }

    public IReadOnlyList<SearchResult> Results { get; }

    public string? Error { get; }

    public int InvocationCount { get; }

    public bool IsSuccess => string.IsNullOrWhiteSpace(Error);
}
