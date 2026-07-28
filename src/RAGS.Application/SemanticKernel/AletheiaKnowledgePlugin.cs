using System.ComponentModel;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.SemanticKernel;

namespace Aletheia.RAGS.Application.SemanticKernel;

/// <summary>
/// Semantic Kernel plugin that exposes the Aletheia Knowledge Estate (RAGS, GraphRAG, LazyGraphRAG,
/// global graph search, and source resolution/ingestion) as callable agentic tools.
/// </summary>
public sealed class AletheiaKnowledgePlugin
{
    private readonly IRagsService _ragsService;
    private readonly IGraphRagService _graphRagService;
    private readonly ILazyGraphRagService _lazyGraphRagService;
    private readonly IGlobalGraphSearchService _globalGraphSearchService;
    private readonly IKnowledgeSourceResolver? _knowledgeSourceResolver;
    private readonly IKnowledgeSourceIngestionService? _knowledgeSourceIngestionService;

    public AletheiaKnowledgePlugin(
        IRagsService ragsService,
        IGraphRagService graphRagService,
        ILazyGraphRagService lazyGraphRagService,
        IGlobalGraphSearchService globalGraphSearchService,
        IKnowledgeSourceResolver? knowledgeSourceResolver = null,
        IKnowledgeSourceIngestionService? knowledgeSourceIngestionService = null)
    {
        _ragsService = ragsService ?? throw new ArgumentNullException(nameof(ragsService));
        _graphRagService = graphRagService ?? throw new ArgumentNullException(nameof(graphRagService));
        _lazyGraphRagService = lazyGraphRagService ?? throw new ArgumentNullException(nameof(lazyGraphRagService));
        _globalGraphSearchService = globalGraphSearchService ?? throw new ArgumentNullException(nameof(globalGraphSearchService));
        _knowledgeSourceResolver = knowledgeSourceResolver;
        _knowledgeSourceIngestionService = knowledgeSourceIngestionService;
    }

    /// <summary>
    /// Search the local RAGS vector index for chunks relevant to the query.
    /// </summary>
    [KernelFunction]
    [Description("Search the local RAGS vector index for relevant chunks. Returns cited search results from registered repository artifacts.")]
    public async Task<IReadOnlyList<SearchResult>> SearchRagsAsync(
        [Description("The user's question or search query.")] string query,
        [Description("Maximum number of chunks to return."), DefaultValue(8)] int topK,
        [Description("Optional source artifact ID to scope the search.")] string? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<SearchResult>();
        }

        Guid? resolvedSourceId = null;
        if (!string.IsNullOrWhiteSpace(sourceId) && Guid.TryParse(sourceId, out var parsed))
        {
            resolvedSourceId = parsed;
        }

        var result = await _ragsService.RetrieveAsync(
            new RetrievalRequest(query, Math.Clamp(topK, 1, 50), resolvedSourceId),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess && result.Value is not null
            ? result.Value
            : Array.Empty<SearchResult>();
    }

    /// <summary>
    /// Search the GraphRAG community summaries for a corpus-level answer.
    /// </summary>
    [KernelFunction]
    [Description("Search the GraphRAG community summaries for corpus-level answers. Use this for broad questions that span many documents.")]
    public async Task<GlobalSearchResult> SearchGraphRagAsync(
        [Description("The user's question or search query.")] string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new GlobalSearchResult("Query is required.", Array.Empty<string>(), Array.Empty<SearchResult>());
        }

        var result = await _graphRagService.GlobalSearchAsync(query, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess && result.Value is not null
            ? result.Value
            : new GlobalSearchResult(result.Error ?? "GraphRAG search returned no results.", Array.Empty<string>(), Array.Empty<SearchResult>());
    }

    /// <summary>
    /// Search the LazyGraphRAG index for a corpus-level answer.
    /// </summary>
    [KernelFunction]
    [Description("Search the LazyGraphRAG index for corpus-level answers. Use this when GraphRAG communities have not been fully built.")]
    public async Task<GlobalSearchResult> SearchLazyGraphRagAsync(
        [Description("The user's question or search query.")] string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new GlobalSearchResult("Query is required.", Array.Empty<string>(), Array.Empty<SearchResult>());
        }

        var result = await _lazyGraphRagService.GlobalSearchAsync(query, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess && result.Value is not null
            ? result.Value
            : new GlobalSearchResult(result.Error ?? "LazyGraphRAG search returned no results.", Array.Empty<string>(), Array.Empty<SearchResult>());
    }

    /// <summary>
    /// Run a global graph search across the entire Aletheia Knowledge Estate.
    /// </summary>
    [KernelFunction]
    [Description("Run a global graph search across the entire Aletheia Knowledge Estate for broad, organization-wide summaries.")]
    public async Task<GlobalSearchResult> SearchGlobalGraphAsync(
        [Description("The user's question or search query.")] string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new GlobalSearchResult("Query is required.", Array.Empty<string>(), Array.Empty<SearchResult>());
        }

        var result = await _globalGraphSearchService.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess && result.Value is not null
            ? result.Value
            : new GlobalSearchResult(result.Error ?? "Global graph search returned no results.", Array.Empty<string>(), Array.Empty<SearchResult>());
    }

    /// <summary>
    /// Resolve the most relevant knowledge source for a user query.
    /// </summary>
    [KernelFunction]
    [Description("Resolve the most relevant registered knowledge source (artifact) for a user query.")]
    public async Task<KnowledgeSource?> ResolveKnowledgeSourceAsync(
        [Description("The user's question.")] string userMessage,
        CancellationToken cancellationToken = default)
    {
        if (_knowledgeSourceResolver is null || string.IsNullOrWhiteSpace(userMessage))
        {
            return null;
        }

        var result = await _knowledgeSourceResolver.ResolveAsync(userMessage, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>
    /// Ensure a resolved knowledge source is ingested into the search index.
    /// </summary>
    [KernelFunction]
    [Description("Ensure a resolved knowledge source is ingested into the search index so it can be retrieved.")]
    public async Task<bool> EnsureSourceIngestedAsync(
        [Description("The source ID of the artifact to ingest."), DefaultValue("00000000-0000-0000-0000-000000000000")] string sourceId,
        CancellationToken cancellationToken = default)
    {
        if (_knowledgeSourceIngestionService is null || !Guid.TryParse(sourceId, out var parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        var source = new KnowledgeSource(parsed, "resolved-source", DateTimeOffset.UtcNow);
        var result = await _knowledgeSourceIngestionService.EnsureIngestedAsync(source, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess && result.Value;
    }
}
