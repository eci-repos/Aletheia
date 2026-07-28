using System.Net.Http.Json;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Aletheia.Foundation.Shared;
using Aletheia.KnowledgeGraph.Abstractions.Models;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Web.Services;

public sealed class RepositoryApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AuthService _authService;

    public RepositoryApiClient(IHttpClientFactory httpClientFactory, AuthService authService)
    {
        _httpClient = httpClientFactory.CreateClient("RepositoryApi");
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    public async Task<SearchResponse?> SearchAsync(string? query, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/search?query={Uri.EscapeDataString(query ?? "")}&pageNumber={pageNumber}&pageSize={pageSize}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<SearchResponse>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<FileMetadata?> GetMetadataAsync(Guid fileId, string fileName, string? version, CancellationToken cancellationToken = default)
    {
        var url = $"/api/metadata?fileId={fileId}&fileName={Uri.EscapeDataString(fileName)}";
        if (!string.IsNullOrWhiteSpace(version))
        {
            url += $"&version={Uri.EscapeDataString(version)}";
        }

        var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<FileMetadata>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<UploadClientResult> UploadAsync(Guid fileId, string fileName, string contentType, Stream content, long sizeBytes, CancellationToken cancellationToken = default)
    {
        var form = new MultipartFormDataContent
        {
            { new StreamContent(content), "file", fileName },
            { new StringContent(fileId.ToString()), "fileId" },
            { new StringContent(fileName), "fileName" },
            { new StringContent(contentType), "contentType" },
            { new StringContent(sizeBytes.ToString()), "sizeBytes" }
        };

        var response = await _httpClient.PostAsync("/api/files/upload", form, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await BuildApiFailureAsync(
                response,
                "upload",
                "POST /api/files/upload",
                cancellationToken).ConfigureAwait(false);
            return new UploadClientResult(false, false, false, "UploadFailed", error);
        }

        var uploadResult = await response.Content.ReadFromJsonAsync<UploadApiResult>(cancellationToken).ConfigureAwait(false);
        return new UploadClientResult(
            true,
            uploadResult?.RagsIngested == true,
            uploadResult?.KnowledgeIndexed == true,
            uploadResult?.IngestionStatus ?? "Unknown",
            uploadResult?.IngestionError,
            uploadResult?.IngestionJobId);
    }

    public async Task<Stream?> DownloadAsync(Guid fileId, string fileName, string? version, CancellationToken cancellationToken = default)
    {
        var url = $"/api/files/download?fileId={fileId}&fileName={Uri.EscapeDataString(fileName)}";
        if (!string.IsNullOrWhiteSpace(version))
        {
            url += $"&version={Uri.EscapeDataString(version)}";
        }

        var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(Guid fileId, string fileName, string? version, CancellationToken cancellationToken = default)
    {
        var url = $"/api/files?fileId={fileId}&fileName={Uri.EscapeDataString(fileName)}";
        if (!string.IsNullOrWhiteSpace(version))
        {
            url += $"&version={Uri.EscapeDataString(version)}";
        }

        var response = await _httpClient.DeleteAsync(url, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyCollection<FileDescriptor>?> ListVersionsAsync(Guid fileId, string fileName, string? version, CancellationToken cancellationToken = default)
    {
        var url = $"/api/versions?fileId={fileId}&fileName={Uri.EscapeDataString(fileName)}";
        if (!string.IsNullOrWhiteSpace(version))
        {
            url += $"&version={Uri.EscapeDataString(version)}";
        }

        var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<IReadOnlyCollection<FileDescriptor>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<FileDescriptor?> CreateVersionAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/versions/create", descriptor, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<FileDescriptor>(cancellationToken).ConfigureAwait(false);
    }

    // RAGS
    public async Task<bool> RagsIngestAsync(Guid sourceId, string content, CancellationToken cancellationToken = default)
    {
        var request = new { sourceId, content };
        var response = await _httpClient.PostAsJsonAsync("/api/rags/ingest", request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async Task<BackgroundJobClientSnapshot?> RagsIngestJobAsync(Guid sourceId, string content, CancellationToken cancellationToken = default)
    {
        var request = new { sourceId, content };
        var response = await _httpClient.PostAsJsonAsync("/api/jobs/rags/ingest", request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildApiFailureAsync(
                response,
                "RAGS ingestion",
                "POST /api/jobs/rags/ingest",
                cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<BackgroundJobClientSnapshot>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SearchResult>?> RagsRetrieveAsync(string query, int topK = 5, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/rags/retrieve?query={Uri.EscapeDataString(query)}&topK={topK}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildApiFailureAsync(
                response,
                "RAGS retrieval",
                "GET /api/rags/retrieve",
                cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<SearchResult>>(cancellationToken).ConfigureAwait(false);
    }

    // GraphRAG
    public async Task<bool> GraphRagIngestAsync(Guid sourceId, string content, CancellationToken cancellationToken = default)
    {
        var request = new { sourceId, content };
        var response = await _httpClient.PostAsJsonAsync("/api/graphrag/ingest", request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async Task<BackgroundJobClientSnapshot?> GraphRagIngestJobAsync(Guid sourceId, string content, CancellationToken cancellationToken = default)
    {
        var request = new { sourceId, content };
        var response = await _httpClient.PostAsJsonAsync("/api/jobs/graphrag/ingest", request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildApiFailureAsync(
                response,
                "GraphRAG ingestion",
                "POST /api/jobs/graphrag/ingest",
                cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<BackgroundJobClientSnapshot>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SearchResult>?> GraphRagRetrieveAsync(string query, int topK = 5, int maxExpanded = 10, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/graphrag/retrieve?query={Uri.EscapeDataString(query)}&topK={topK}&maxExpanded={maxExpanded}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildApiFailureAsync(
                response,
                "GraphRAG retrieval",
                "GET /api/graphrag/retrieve",
                cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<SearchResult>>(cancellationToken).ConfigureAwait(false);
    }

    // LazyGraphRAG
    public async Task<bool> LazyGraphRagIngestAsync(Guid sourceId, string content, CancellationToken cancellationToken = default)
    {
        var request = new { sourceId, content };
        var response = await _httpClient.PostAsJsonAsync("/api/lazygraphrag/ingest", request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async Task<BackgroundJobClientSnapshot?> LazyGraphRagIngestJobAsync(Guid sourceId, string content, CancellationToken cancellationToken = default)
    {
        var request = new { sourceId, content };
        var response = await _httpClient.PostAsJsonAsync("/api/jobs/lazygraphrag/ingest", request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildApiFailureAsync(
                response,
                "LazyGraphRAG ingestion",
                "POST /api/jobs/lazygraphrag/ingest",
                cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<BackgroundJobClientSnapshot>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SearchResult>?> LazyGraphRagRetrieveAsync(string query, int topK = 5, int maxExpanded = 10, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/lazygraphrag/retrieve?query={Uri.EscapeDataString(query)}&topK={topK}&maxExpanded={maxExpanded}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildApiFailureAsync(
                response,
                "LazyGraphRAG retrieval",
                "GET /api/lazygraphrag/retrieve",
                cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<SearchResult>>(cancellationToken).ConfigureAwait(false);
    }

    // WRAGS Wiki
    public async Task<IReadOnlyList<WikiPage>?> SearchWikiAsync(WikiSearchRequest request, CancellationToken cancellationToken = default)
    {
        var query = Uri.EscapeDataString(request.Query ?? string.Empty);
        var mode = Uri.EscapeDataString(request.Mode ?? "wrags");
        var response = await _httpClient.GetAsync(
            $"/api/wiki/search?query={query}&mode={mode}&topK={request.TopK}&expansion={request.Expansion}&regenerate={request.Regenerate}",
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildApiFailureAsync(
                response,
                "WRAGS wiki search",
                "GET /api/wiki/search",
                cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<WikiPage>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WikiPage>?> RegenerateWikiAsync(WikiSearchRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/wiki/regenerate", request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildApiFailureAsync(
                response,
                "WRAGS wiki regeneration",
                "POST /api/wiki/regenerate",
                cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<WikiPage>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<BackgroundJobClientSnapshot?> RegenerateWikiJobAsync(WikiSearchRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/wiki/regenerate/job", request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildApiFailureAsync(
                response,
                "WRAGS wiki regeneration job",
                "POST /api/wiki/regenerate/job",
                cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<BackgroundJobClientSnapshot>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SearchResult>?> WragsRetrieveAsync(string query, int topK = 5, int expansion = 1, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/wiki/retrieve?query={Uri.EscapeDataString(query)}&mode=wrags&topK={topK}&expansion={expansion}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildApiFailureAsync(
                response,
                "WRAGS retrieval",
                "GET /api/wiki/retrieve",
                cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<SearchResult>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WikiPage>?> GetRecentWikiPagesAsync(int take = 20, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/wiki/recent?take={take}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildApiFailureAsync(
                response,
                "WRAGS recent pages",
                "GET /api/wiki/recent",
                cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<WikiPage>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WikiPageLink>?> GetRelatedWikiPagesAsync(Guid pageId, int take = 10, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/wiki/pages/{pageId}/related?take={take}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildApiFailureAsync(
                response,
                "WRAGS related pages",
                $"GET /api/wiki/pages/{pageId}/related",
                cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<WikiPageLink>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<WikiPage?> UpdateWikiPageStatusAsync(Guid pageId, WikiPageStatusUpdate update, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PatchAsJsonAsync($"/api/wiki/pages/{pageId}/status", update, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildApiFailureAsync(
                response,
                "WRAGS status update",
                $"PATCH /api/wiki/pages/{pageId}/status",
                cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<WikiPage>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<WikiPage?> UpdateWikiPageAsync(Guid pageId, WikiPageEditRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/wiki/pages/{pageId}", request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildApiFailureAsync(
                response,
                "WRAGS page edit",
                $"PUT /api/wiki/pages/{pageId}",
                cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<WikiPage>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WikiPageHistoryEntry>?> GetWikiPageHistoryAsync(Guid pageId, int take = 20, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/wiki/pages/{pageId}/history?take={take}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildApiFailureAsync(
                response,
                "WRAGS page history",
                $"GET /api/wiki/pages/{pageId}/history",
                cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<WikiPageHistoryEntry>>(cancellationToken).ConfigureAwait(false);
    }

    // Taxonomy
    public async Task<IReadOnlyCollection<string>?> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/api/taxonomy/categories", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<IReadOnlyCollection<string>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<string>?> GetTagsAsync(string category, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/taxonomy/categories/{Uri.EscapeDataString(category)}/tags", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<IReadOnlyCollection<string>>(cancellationToken).ConfigureAwait(false);
    }

    // Ontology
    public async Task<IReadOnlyCollection<string>?> GetEntitiesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/api/ontology/entities", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<IReadOnlyCollection<string>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, string>?> GetRelationshipsAsync(string entity, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/ontology/entities/{Uri.EscapeDataString(entity)}/relationships", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<IReadOnlyDictionary<string, string>>(cancellationToken).ConfigureAwait(false);
    }

    // Knowledge Graph
    public async Task<GraphImportResult> GraphImportAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync("/api/graph/import", null, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return new GraphImportResult(true, null);
        }

        return new GraphImportResult(false, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<List<GraphNode>?> GraphGetNodesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/api/graph/nodes", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<List<GraphNode>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<GraphEdge>?> GraphGetEdgesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/api/graph/edges", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<List<GraphEdge>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<GraphNode>?> GraphGetNeighborsAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/graph/nodes/{Uri.EscapeDataString(nodeId)}/neighbors", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<List<GraphNode>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<GraphPath>?> GraphFindPathAsync(string from, string to, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/graph/path?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<List<GraphPath>>(cancellationToken).ConfigureAwait(false);
    }

    // Background jobs
    public async Task<IReadOnlyList<BackgroundJobClientSnapshot>?> GetIngestionJobsAsync(int take = 50, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/jobs?take={take}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<BackgroundJobClientSnapshot>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<BackgroundJobClientSnapshot?> GetIngestionJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/jobs/{jobId}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<BackgroundJobClientSnapshot>(cancellationToken).ConfigureAwait(false);
    }

    // Copilot planning
    public async Task<ChatPlanRecord?> PlanChatAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/copilot/plan", new { Prompt = prompt }, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ChatPlanRecord>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChatPlanRecord?> ApproveChatPlanAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"/api/copilot/plans/{planId}/approve", null, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ChatPlanRecord>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChatPlanRecord?> CancelChatPlanAsync(Guid planId, string? reason = null, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/copilot/plans/{planId}/cancel", new { Reason = reason }, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ChatPlanRecord>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChatJobSnapshot?> ExecuteChatPlanAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"/api/copilot/plans/{planId}/execute", null, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ChatJobSnapshot>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChatJobSnapshot?> GetChatJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/copilot/jobs/chat/{jobId}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ChatJobSnapshot>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> CancelChatJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"/api/copilot/jobs/chat/{jobId}/cancel", null, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async Task<ChatProgressRecord?> GetChatPlanProgressAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/copilot/plans/{planId}/progress", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ChatProgressRecord>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChatExecutionTelemetry?> GetChatJobTelemetryAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/copilot/jobs/chat/{jobId}/telemetry", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ChatExecutionTelemetry>(cancellationToken).ConfigureAwait(false);
    }

    // Copilot chat
    public async Task<ChatMessage?> ChatAsync(ChatSession session, string message, string? outputFormat = null, CancellationToken cancellationToken = default)
    {
        var token = await _authService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var request = new { Session = session, Message = message, OutputFormat = outputFormat };
        _httpClient.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
        var response = await _httpClient.PostAsJsonAsync("/api/copilot/chat", request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildApiFailureAsync(
                response,
                "Copilot chat",
                "POST /api/copilot/chat",
                cancellationToken).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<ChatMessage>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SummaryResponse?> SummarizeAsync(string query, CancellationToken cancellationToken = default)
    {
        var request = new SummaryRequest { Query = query };
        var response = await _httpClient.PostAsJsonAsync("/api/copilot/summarize", request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<SummaryResponse>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExplanationResponse?> ExplainAsync(string query, CancellationToken cancellationToken = default)
    {
        var request = new ExplanationRequest { Query = query };
        var response = await _httpClient.PostAsJsonAsync("/api/copilot/explain", request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ExplanationResponse>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DiscoveryResponse?> DiscoverAsync(string topic, CancellationToken cancellationToken = default)
    {
        var request = new DiscoveryRequest { Topic = topic };
        var response = await _httpClient.PostAsJsonAsync("/api/copilot/discover", request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<DiscoveryResponse>(cancellationToken).ConfigureAwait(false);
    }

    // Collaboration - Comments
    public async Task<IReadOnlyList<Comment>?> GetCommentsAsync(string targetId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/collaboration/comments?targetId={Uri.EscapeDataString(targetId)}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<Comment>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Comment?> AddCommentAsync(Comment comment, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/collaboration/comments", comment, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<Comment>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteCommentAsync(Guid commentId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/collaboration/comments/{commentId}", cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    // Collaboration - Annotations
    public async Task<IReadOnlyList<Annotation>?> GetAnnotationsAsync(string targetId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/collaboration/annotations?targetId={Uri.EscapeDataString(targetId)}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<Annotation>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Annotation?> AddAnnotationAsync(Annotation annotation, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/collaboration/annotations", annotation, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<Annotation>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAnnotationAsync(Guid annotationId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/collaboration/annotations/{annotationId}", cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    // Collaboration - Bookmarks
    public async Task<IReadOnlyList<Bookmark>?> GetBookmarksAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/collaboration/bookmarks?userId={Uri.EscapeDataString(userId)}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<Bookmark>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Bookmark?> AddBookmarkAsync(Bookmark bookmark, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/collaboration/bookmarks", bookmark, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<Bookmark>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveBookmarkAsync(Guid bookmarkId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/collaboration/bookmarks/{bookmarkId}", cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    // Collaboration - Collections
    public async Task<IReadOnlyList<Collection>?> GetCollectionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/collaboration/collections?userId={Uri.EscapeDataString(userId)}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<Collection>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Collection?> CreateCollectionAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/collaboration/collections", collection, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<Collection>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Collection?> UpdateCollectionAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync("/api/collaboration/collections", collection, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<Collection>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/collaboration/collections/{collectionId}", cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    // Collaboration - Workspaces
    public async Task<IReadOnlyList<SharedWorkspace>?> GetWorkspacesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/collaboration/workspaces?userId={Uri.EscapeDataString(userId)}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<SharedWorkspace>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SharedWorkspace?> CreateWorkspaceAsync(SharedWorkspace workspace, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/collaboration/workspaces", workspace, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<SharedWorkspace>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SharedWorkspace?> UpdateWorkspaceAsync(SharedWorkspace workspace, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync("/api/collaboration/workspaces", workspace, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<SharedWorkspace>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/collaboration/workspaces/{workspaceId}", cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ShareWorkspaceAsync(Guid workspaceId, string userId, string role, CancellationToken cancellationToken = default)
    {
        var payload = new { UserId = userId, Role = role };
        var response = await _httpClient.PostAsJsonAsync($"/api/collaboration/workspaces/{workspaceId}/share", payload, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    // Governance - Roles
    public async Task<IReadOnlyList<Role>?> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/api/governance/roles", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<Role>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Role?> CreateRoleAsync(Role role, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/governance/roles", role, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<Role>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/governance/roles/{roleId}", cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    // Governance - Audit Logs
    public async Task<IReadOnlyList<AuditLog>?> GetAuditLogsAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (from.HasValue) query.Add($"from={Uri.EscapeDataString(from.Value.ToString("O"))}");
        if (to.HasValue) query.Add($"to={Uri.EscapeDataString(to.Value.ToString("O"))}");
        var url = "/api/governance/audit-logs" + (query.Count > 0 ? $"?{string.Join("&", query)}" : "");
        var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<AuditLog>>(cancellationToken).ConfigureAwait(false);
    }

    // Governance - Retention Policies
    public async Task<IReadOnlyList<RetentionPolicy>?> GetRetentionPoliciesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/api/governance/retention-policies", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<RetentionPolicy>>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RetentionPolicy?> CreateRetentionPolicyAsync(RetentionPolicy policy, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/governance/retention-policies", policy, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<RetentionPolicy>(cancellationToken).ConfigureAwait(false);
    }

    // Governance - PII Scan
    public async Task<PiiDetectionResult?> ScanPiiAsync(string content, CancellationToken cancellationToken = default)
    {
        var payload = new { Content = content };
        var response = await _httpClient.PostAsJsonAsync("/api/governance/scan-pii", payload, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<PiiDetectionResult>(cancellationToken).ConfigureAwait(false);
    }

    private sealed record UploadApiResult(bool RagsIngested, bool KnowledgeIndexed, string IngestionStatus, string? IngestionError, Guid? IngestionJobId);

    private static async Task<string> BuildApiFailureAsync(
        HttpResponseMessage response,
        string operation,
        string endpoint,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var details = ExtractErrorDetails(body);
        var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase) ? "Unknown reason" : response.ReasonPhrase;

        return string.IsNullOrWhiteSpace(details)
            ? $"{operation} failed. HTTP {(int)response.StatusCode} {reason}. Endpoint: {endpoint}."
            : $"{operation} failed. HTTP {(int)response.StatusCode} {reason}. Endpoint: {endpoint}. {details}";
    }

    private static string ExtractErrorDetails(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var parts = new List<string>();

            AddJsonString(parts, root, "error", "Error");
            AddJsonString(parts, root, "title", "Title");
            AddJsonString(parts, root, "detail", "Detail");
            AddJsonString(parts, root, "traceId", "TraceId");

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var validationErrors = errors.EnumerateObject()
                    .Select(error => $"{error.Name}: {FlattenJsonValue(error.Value)}")
                    .Where(error => !string.IsNullOrWhiteSpace(error))
                    .ToList();

                if (validationErrors.Count > 0)
                {
                    parts.Add($"Validation: {string.Join("; ", validationErrors)}");
                }
            }

            return parts.Count == 0
                ? $"ResponseBody: {TrimForDisplay(body)}"
                : string.Join(" ", parts);
        }
        catch (JsonException)
        {
            return $"ResponseBody: {TrimForDisplay(body)}";
        }
    }

    private static void AddJsonString(List<string> parts, JsonElement root, string propertyName, string label)
    {
        if (root.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString()))
        {
            parts.Add($"{label}: {value.GetString()}");
        }
    }

    private static string FlattenJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(FlattenJsonValue)),
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Null => string.Empty,
            _ => value.ToString()
        };
    }

    private static string TrimForDisplay(string value)
    {
        const int maxLength = 2_000;
        var compact = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return compact.Length <= maxLength ? compact : $"{compact[..maxLength]}...";
    }
}

public sealed record UploadClientResult(bool Uploaded, bool RagsIngested, bool KnowledgeIndexed, string IngestionStatus, string? Error, Guid? IngestionJobId = null);
public sealed record GraphImportResult(bool IsSuccess, string? Error);
public sealed record BackgroundJobClientSnapshot(
    Guid JobId,
    string Kind,
    string Title,
    string Status,
    string Stage,
    int PercentComplete,
    int CompletedUnits,
    int TotalUnits,
    string Detail,
    Guid SourceId,
    string? SourceName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset LastHeartbeatAt,
    DateTimeOffset? CompletedAt,
    string? Error);
