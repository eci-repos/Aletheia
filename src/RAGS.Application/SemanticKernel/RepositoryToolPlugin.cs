using System.ComponentModel;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.SemanticKernel;

namespace Aletheia.RAGS.Application.SemanticKernel;

/// <summary>
/// Alternative plugin name for the Aletheia Knowledge Estate tool suite.
/// This is a thin shim over <see cref="AletheiaKnowledgePlugin"/>, registered under
/// the "RepositoryTool" plugin name so that planners and prompts can refer to it as the
/// repository/local-knowledge tool.
/// </summary>
public sealed class RepositoryToolPlugin
{
    private readonly AletheiaKnowledgePlugin _inner;

    public RepositoryToolPlugin(AletheiaKnowledgePlugin inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>
    /// Search repository documents for chunks relevant to the query.
    /// </summary>
    [KernelFunction]
    [Description("Search registered repository documents for relevant chunks. Returns cited search results from the Aletheia Knowledge Estate." +
                 " Use this for questions about RFPs, contracts, requirements, or any repository-specific topic." +
                 " You must call this tool before answering repository-specific questions." +
                 " Do not answer from general knowledge." +
                 " Returns up to topK cited chunks from the local RAGS index.")]
    public Task<IReadOnlyList<SearchResult>> SearchRepositoryDocumentsAsync(
        [Description("The user's question or search query. Must be repository-specific."), DefaultValue("What are the RFP requirements?")] string query,
        [Description("Maximum number of chunks to return."), DefaultValue(8)] int topK = 8,
        CancellationToken cancellationToken = default)
    {
        return _inner.SearchRagsAsync(query, topK, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Search the GraphRAG community summaries for a corpus-level answer.
    /// </summary>
    [KernelFunction]
    [Description("Search GraphRAG community summaries for a corpus-level answer to broad questions spanning many documents." +
                 " Use this when the question covers trends, timelines, or aggregate patterns across the repository." +
                 " Do not answer from general knowledge.")]
    public Task<GlobalSearchResult> SearchRepositoryGraphRagAsync(
        [Description("The user's question or search query. Must be repository-specific.")] string query,
        CancellationToken cancellationToken = default)
    {
        return _inner.SearchGraphRagAsync(query, cancellationToken);
    }

    /// <summary>
    /// Resolve the most relevant registered knowledge source for a user query.
    /// </summary>
    [KernelFunction]
    [Description("Resolve the most relevant registered knowledge source (artifact) for a user query." +
                 " Use this to identify which repository document to retrieve before searching.")]
    public Task<KnowledgeSource?> ResolveRepositorySourceAsync(
        [Description("The user's question."), DefaultValue("What are the RFP requirements?")] string userMessage,
        CancellationToken cancellationToken = default)
    {
        return _inner.ResolveKnowledgeSourceAsync(userMessage, cancellationToken);
    }
}
