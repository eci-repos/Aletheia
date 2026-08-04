using System.Collections.Concurrent;
using System.Threading.Channels;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Microsoft.Extensions.Hosting;

namespace Aletheia.Repository.API.Services;

public interface IIngestionJobService
{
    IngestionJobSnapshot EnqueueUploadedFile(
        Guid sourceId,
        string sourceName,
        string contentType,
        string tempFilePath,
        long sizeBytes);

    IngestionJobSnapshot EnqueueContent(
        IngestionJobEngine engine,
        Guid sourceId,
        string content,
        string? sourceName = null);

    IngestionJobSnapshot EnqueueWikiRegeneration(WikiSearchRequest request);

    IngestionJobSnapshot EnqueueRagsRepair(string? query = null);

    IngestionJobSnapshot EnqueueDocumentBriefs(Guid? sourceId = null, string? sourceName = null);

    IReadOnlyList<IngestionJobSnapshot> List(int take = 50);

    IngestionJobSnapshot? Get(Guid jobId);
}

public enum IngestionJobEngine
{
    Rags,
    GraphRag,
    LazyGraphRag,
    WikiRegeneration,
    RagsRepair,
    DocumentBriefs
}

public sealed record IngestionJobSnapshot(
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

internal sealed class IngestionJobService : BackgroundService, IIngestionJobService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(2);
    private const int MaxJobs = 200;

    private readonly Channel<IngestionJobWorkItem> _queue = Channel.CreateUnbounded<IngestionJobWorkItem>();
    private readonly ConcurrentDictionary<Guid, IngestionJobState> _jobs = new();
    private readonly ConcurrentQueue<Guid> _jobOrder = new();
    private readonly IUploadedFileTextExtractor _textExtractor;
    private readonly IRagsService _ragsService;
    private readonly IGraphRagService _graphRagService;
    private readonly ILazyGraphRagService _lazyGraphRagService;
    private readonly IWragsWikiService _wikiService;
    private readonly IUploadedContentKnowledgeIndexer _knowledgeIndexer;
    private readonly IMetadataRepository _metadataRepository;
    private readonly IKnowledgeSourceIngestionService _knowledgeSourceIngestionService;
    private readonly IDocumentBriefService _documentBriefService;
    private readonly ILogger<IngestionJobService> _logger;

    public IngestionJobService(
        IUploadedFileTextExtractor textExtractor,
        IRagsService ragsService,
        IGraphRagService graphRagService,
        ILazyGraphRagService lazyGraphRagService,
        IWragsWikiService wikiService,
        IUploadedContentKnowledgeIndexer knowledgeIndexer,
        IMetadataRepository metadataRepository,
        IKnowledgeSourceIngestionService knowledgeSourceIngestionService,
        IDocumentBriefService documentBriefService,
        ILogger<IngestionJobService> logger)
    {
        _textExtractor = textExtractor ?? throw new ArgumentNullException(nameof(textExtractor));
        _ragsService = ragsService ?? throw new ArgumentNullException(nameof(ragsService));
        _graphRagService = graphRagService ?? throw new ArgumentNullException(nameof(graphRagService));
        _lazyGraphRagService = lazyGraphRagService ?? throw new ArgumentNullException(nameof(lazyGraphRagService));
        _wikiService = wikiService ?? throw new ArgumentNullException(nameof(wikiService));
        _knowledgeIndexer = knowledgeIndexer ?? throw new ArgumentNullException(nameof(knowledgeIndexer));
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
        _knowledgeSourceIngestionService = knowledgeSourceIngestionService ?? throw new ArgumentNullException(nameof(knowledgeSourceIngestionService));
        _documentBriefService = documentBriefService ?? throw new ArgumentNullException(nameof(documentBriefService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IngestionJobSnapshot EnqueueUploadedFile(
        Guid sourceId,
        string sourceName,
        string contentType,
        string tempFilePath,
        long sizeBytes)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Source ID is required.", nameof(sourceId));
        }

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("Source name is required.", nameof(sourceName));
        }

        if (string.IsNullOrWhiteSpace(tempFilePath))
        {
            throw new ArgumentException("Temp file path is required.", nameof(tempFilePath));
        }

        var item = IngestionJobWorkItem.ForUploadedFile(sourceId, sourceName, contentType, tempFilePath, sizeBytes);
        return Enqueue(item);
    }

    public IngestionJobSnapshot EnqueueRagsRepair(string? query = null)
    {
        var item = IngestionJobWorkItem.ForRagsRepair(query);
        return Enqueue(item);
    }

    public IngestionJobSnapshot EnqueueDocumentBriefs(Guid? sourceId = null, string? sourceName = null)
    {
        if (sourceId.HasValue && sourceId.Value == Guid.Empty)
        {
            throw new ArgumentException("Source ID is required.", nameof(sourceId));
        }

        var item = IngestionJobWorkItem.ForDocumentBriefs(sourceId, sourceName);
        return Enqueue(item);
    }

    public IngestionJobSnapshot EnqueueContent(
        IngestionJobEngine engine,
        Guid sourceId,
        string content,
        string? sourceName = null)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Source ID is required.", nameof(sourceId));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content is required.", nameof(content));
        }

        var item = IngestionJobWorkItem.ForContent(engine, sourceId, content, sourceName);
        return Enqueue(item);
    }

    public IngestionJobSnapshot EnqueueWikiRegeneration(WikiSearchRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ArgumentException("Wiki regeneration query is required.", nameof(request));
        }

        var item = IngestionJobWorkItem.ForWikiRegeneration(request);
        return Enqueue(item);
    }

    public IReadOnlyList<IngestionJobSnapshot> List(int take = 50)
    {
        var resolvedTake = Math.Clamp(take, 1, MaxJobs);
        return _jobs.Values
            .Select(state => state.ToSnapshot())
            .OrderByDescending(job => job.CreatedAt)
            .Take(resolvedTake)
            .ToList();
    }

    public IngestionJobSnapshot? Get(Guid jobId)
    {
        return _jobs.TryGetValue(jobId, out var state)
            ? state.ToSnapshot()
            : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            if (!_jobs.TryGetValue(item.JobId, out var state))
            {
                continue;
            }

            try
            {
                await RunJobAsync(item, state, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                state.Cancel("API host is shutting down.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ingestion job {JobId} failed.", item.JobId);
                state.Fail("Failed", ex.Message);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(item.TempFilePath))
                {
                    TryDeleteTempFile(item.TempFilePath);
                }
            }
        }
    }

    private IngestionJobSnapshot Enqueue(IngestionJobWorkItem item)
    {
        var state = new IngestionJobState(item.JobId, item.Kind, item.Title, item.SourceId, item.SourceName);
        _jobs[item.JobId] = state;
        _jobOrder.Enqueue(item.JobId);
        TrimOldJobs();

        if (!_queue.Writer.TryWrite(item))
        {
            state.Fail("Queue", "Unable to queue ingestion job.");
        }

        return state.ToSnapshot();
    }

    private async Task RunJobAsync(IngestionJobWorkItem item, IngestionJobState state, CancellationToken cancellationToken)
    {
        state.Start("Starting", "Worker picked up the job.", 1);

        if (item.Content is not null)
        {
            await RunContentJobAsync(item, state, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (item.WikiRequest is not null)
        {
            await RunWikiRegenerationJobAsync(item, state, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (item.Engine == IngestionJobEngine.RagsRepair)
        {
            await RunRagsRepairJobAsync(item, state, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (item.Engine == IngestionJobEngine.DocumentBriefs)
        {
            await RunDocumentBriefsJobAsync(item, state, cancellationToken).ConfigureAwait(false);
            return;
        }

        await RunUploadedFileJobAsync(item, state, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunRagsRepairJobAsync(
        IngestionJobWorkItem item,
        IngestionJobState state,
        CancellationToken cancellationToken)
    {
        var query = item.RepairQuery;
        state.Update("Repository scan", "Scanning registered Repository documents for RAGS index repair.", 5, force: true);
        var sourcesResult = await LoadRepairSourcesAsync(query, cancellationToken).ConfigureAwait(false);
        if (sourcesResult.IsFailure || sourcesResult.Value is null)
        {
            state.Fail("Repository scan", sourcesResult.Error ?? "Unable to scan registered Repository documents.");
            return;
        }

        var sources = sourcesResult.Value;
        if (sources.Count == 0)
        {
            state.Succeed("No sources found", "No registered Repository documents matched the repair scope.");
            return;
        }

        var failed = new List<string>();
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            state.UpdateUnits(
                "RAGS index repair",
                $"Rehydrating searchable chunks for {source.SourceName} ({i + 1}/{sources.Count}).",
                i,
                sources.Count);

            var result = await RunWithHeartbeatAsync(
                state,
                "RAGS index repair",
                $"Still rebuilding searchable chunks for {source.SourceName}.",
                async ct =>
                {
                    var hydrated = await _knowledgeSourceIngestionService.EnsureIngestedAsync(source, ct).ConfigureAwait(false);
                    return hydrated.IsFailure
                        ? Result.Failure(hydrated.Error ?? $"RAGS repair failed for {source.SourceName}.")
                        : Result.Success();
                },
                cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                failed.Add($"{source.SourceName}: {result.Error}");
            }

            state.UpdateUnits(
                "RAGS index repair",
                $"Completed {i + 1} of {sources.Count} registered document(s).",
                i + 1,
                sources.Count);
        }

        if (failed.Count == sources.Count)
        {
            state.Fail("RAGS index repair", $"RAGS repair failed for all {sources.Count} document(s): {string.Join("; ", failed.Take(3))}");
            return;
        }

        var detail = failed.Count == 0
            ? $"RAGS index repair completed for {sources.Count} registered document(s)."
            : $"RAGS index repair completed for {sources.Count - failed.Count} of {sources.Count} registered document(s); {failed.Count} failed.";
        state.Succeed("Repaired", detail);
    }

    private async Task<Result<IReadOnlyList<KnowledgeSource>>> LoadRepairSourcesAsync(string? query, CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var page = 1;
        var sources = new List<KnowledgeSource>();

        while (true)
        {
            var result = await _metadataRepository
                .SearchAsync(new SearchRequest(string.IsNullOrWhiteSpace(query) ? null : query, page, pageSize), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure || result.Value is null)
            {
                return Result<IReadOnlyList<KnowledgeSource>>.Failure(result.Error ?? "Metadata search failed.");
            }

            var items = result.Value.Items;
            sources.AddRange(items.Select(metadata => new KnowledgeSource(
                metadata.Descriptor.FileId,
                metadata.Descriptor.FileName,
                metadata.UploadedAt)));

            if (sources.Count >= result.Value.TotalCount || items.Count == 0)
            {
                break;
            }

            page++;
        }

        return Result<IReadOnlyList<KnowledgeSource>>.Success(
            sources
                .GroupBy(source => source.SourceId)
                .Select(group => group.OrderByDescending(source => source.UploadedAt).First())
                .OrderBy(source => source.SourceName)
                .ToList());
    }

    private async Task RunWikiRegenerationJobAsync(
        IngestionJobWorkItem item,
        IngestionJobState state,
        CancellationToken cancellationToken)
    {
        state.Update("WRAGS regeneration", "Regenerating durable wiki pages from current retrieval knowledge.", 20, force: true);
        var result = await RunWithHeartbeatAsync(
            state,
            "WRAGS regeneration",
            "Still regenerating WRAGS wiki pages.",
            async ct =>
            {
                var pages = await _wikiService.RegenerateAsync(item.WikiRequest!, ct).ConfigureAwait(false);
                return pages.IsFailure
                    ? Result.Failure(pages.Error ?? "WRAGS regeneration failed.")
                    : Result.Success();
            },
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            state.Fail("WRAGS regeneration", result.Error ?? "WRAGS regeneration failed.");
            return;
        }

        state.Succeed("Regenerated", "WRAGS wiki regeneration completed.");
    }

    private async Task RunDocumentBriefsJobAsync(
        IngestionJobWorkItem item,
        IngestionJobState state,
        CancellationToken cancellationToken)
    {
        if (item.SourceId != Guid.Empty && !string.IsNullOrWhiteSpace(item.SourceName))
        {
            state.Update("Document brief", $"Generating the document brief for {item.SourceName}.", 20, force: true);
            var single = await RunWithHeartbeatAsync(
                state,
                "Document brief",
                $"Still generating the document brief for {item.SourceName}.",
                async ct =>
                {
                    var brief = await _documentBriefService.RegenerateAsync(item.SourceId, item.SourceName!, ct).ConfigureAwait(false);
                    return brief.IsSuccess ? Result.Success() : Result.Failure(brief.Error ?? "Document brief generation failed.");
                },
                cancellationToken).ConfigureAwait(false);

            if (single.IsFailure)
            {
                state.Fail("Document brief", single.Error ?? "Document brief generation failed.");
                return;
            }

            state.Succeed("Brief generated", $"Document brief generated for {item.SourceName}.");
            return;
        }

        state.Update("Document briefs", "Scanning registered Repository documents for brief generation.", 5, force: true);
        DocumentBriefRegenerationResult? briefSummary = null;
        var all = await RunWithHeartbeatAsync(
            state,
            "Document briefs",
            "Still generating document briefs.",
            async ct =>
            {
                var allResult = await _documentBriefService.RegenerateAllAsync(
                    progress => state.UpdateUnits(
                        progress.Stage,
                        progress.Detail,
                        progress.Completed,
                        progress.Total),
                    ct).ConfigureAwait(false);
                if (allResult.IsSuccess && allResult.Value is not null)
                {
                    briefSummary = allResult.Value;
                }

                return allResult.IsSuccess ? Result.Success() : Result.Failure(allResult.Error ?? "Document brief generation failed.");
            },
            cancellationToken).ConfigureAwait(false);

        if (all.IsFailure)
        {
            state.Fail("Document briefs", all.Error ?? "Document brief generation failed.");
            return;
        }

        var summary = briefSummary ?? new DocumentBriefRegenerationResult(0, 0, Array.Empty<string>());
        var detail = summary.Generated == summary.TotalDocuments
            ? $"Document briefs generated for {summary.Generated} registered document(s)."
            : $"Document briefs generated for {summary.Generated} of {summary.TotalDocuments} registered document(s); {summary.Skipped.Count} skipped.";
        state.Succeed("Briefs generated", detail);
    }
    private async Task RunUploadedFileJobAsync(
        IngestionJobWorkItem item,
        IngestionJobState state,
        CancellationToken cancellationToken)
    {
        state.Update("Text extraction", "Extracting searchable text from the uploaded artifact.", 10, force: true);

        await using var extractionStream = File.OpenRead(item.TempFilePath!);
        var extractionResult = await _textExtractor
            .ExtractAsync(item.SourceName ?? "uploaded-file", item.ContentType ?? "application/octet-stream", extractionStream, cancellationToken)
            .ConfigureAwait(false);

        if (extractionResult.IsFailure)
        {
            state.Fail("Text extraction", extractionResult.Error ?? "Text extraction failed.");
            return;
        }

        var extraction = extractionResult.Value!;
        if (!extraction.IsSupported)
        {
            state.Succeed(extraction.Status, "File uploaded. RAGS ingestion was skipped for this file type.");
            return;
        }

        if (string.IsNullOrWhiteSpace(extraction.Text))
        {
            state.Succeed("No text extracted", "File uploaded, but no searchable text could be extracted.");
            return;
        }

        var request = new IngestionRequest(item.SourceId, extraction.Text, item.SourceName);
        state.Update("Chunks and embeddings", "Generating chunks and vector embeddings.", 30, force: true);
        var ragsResult = await RunWithHeartbeatAsync(
            state,
            "Chunks and embeddings",
            "Still generating chunks and vector embeddings.",
            ct => _ragsService.IngestAsync(request, ct),
            cancellationToken).ConfigureAwait(false);

        if (ragsResult.IsFailure)
        {
            state.Fail("RAGS ingestion", ragsResult.Error ?? "RAGS ingestion failed.");
            return;
        }

        state.Update("Knowledge seed", "Recording taxonomy hints and graph seed nodes for query-time enrichment.", 55, force: true);
        var progress = new JobProgressSink(state, minimumInterval: HeartbeatInterval);
        var knowledgeResult = await RunWithHeartbeatAsync(
            state,
            "Knowledge seed",
            "Still recording graph seed chunks for lazy enrichment.",
            ct => _knowledgeIndexer.IndexLightweightAsync(item.SourceId, item.SourceName ?? "uploaded-file", extraction.Text, progress, ct),
            cancellationToken).ConfigureAwait(false);

        if (knowledgeResult.IsFailure)
        {
            state.Fail("Knowledge seed", knowledgeResult.Error ?? "Knowledge seed failed.");
            return;
        }

        // Sprint 55: keep the user-facing Wiki fresh with a document brief for this source.
        EnqueueDocumentBriefs(item.SourceId, item.SourceName);

        state.Succeed("Indexed", "File uploaded, made searchable, and prepared for lazy query-time enrichment.");
    }

    private async Task RunContentJobAsync(
        IngestionJobWorkItem item,
        IngestionJobState state,
        CancellationToken cancellationToken)
    {
        var request = new IngestionRequest(item.SourceId, item.Content!, item.SourceName);
        var (stage, detail, operation) = item.Engine switch
        {
            IngestionJobEngine.GraphRag => (
                "GraphRAG lazy seed",
                "Generating chunks, embeddings, and lightweight graph seed nodes for query-time enrichment.",
                new Func<CancellationToken, Task<Result>>(async ct =>
                {
                    var rags = await _ragsService.IngestAsync(request, ct).ConfigureAwait(false);
                    if (rags.IsFailure)
                    {
                        return rags;
                    }

                    return await _knowledgeIndexer
                        .IndexLightweightAsync(item.SourceId, item.SourceName ?? "content", item.Content!, null, ct)
                        .ConfigureAwait(false);
                })),
            IngestionJobEngine.LazyGraphRag => (
                "LazyGraphRAG indexing",
                "Recording chunks and low-cost TF-IDF/BM25 corpus statistics.",
                new Func<CancellationToken, Task<Result>>(ct => _lazyGraphRagService.IngestAsync(request, ct))),
            _ => (
                "Chunks and embeddings",
                "Generating chunks and vector embeddings.",
                new Func<CancellationToken, Task<Result>>(ct => _ragsService.IngestAsync(request, ct)))
        };

        state.Update(stage, detail, 15, force: true);
        var result = await RunWithHeartbeatAsync(
            state,
            stage,
            $"Still running {stage.ToLowerInvariant()}.",
            operation,
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            state.Fail(stage, result.Error ?? "Ingestion failed.");
            return;
        }

        state.Succeed("Indexed", "Content ingestion completed.");
    }

    private static async Task<Result> RunWithHeartbeatAsync(
        IngestionJobState state,
        string stage,
        string detail,
        Func<CancellationToken, Task<Result>> operation,
        CancellationToken cancellationToken)
    {
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(HeartbeatInterval);
            while (await timer.WaitForNextTickAsync(heartbeatCts.Token).ConfigureAwait(false))
            {
                state.Update(stage, detail, null, force: true);
            }
        }, heartbeatCts.Token);

        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await heartbeatCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private void TrimOldJobs()
    {
        while (_jobs.Count > MaxJobs && _jobOrder.TryDequeue(out var oldJobId))
        {
            if (_jobs.TryGetValue(oldJobId, out var state) && !state.IsActive)
            {
                _jobs.TryRemove(oldJobId, out _);
            }
        }
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed class JobProgressSink : IIngestionProgressSink
    {
        private readonly IngestionJobState _state;
        private readonly TimeSpan _minimumInterval;
        private DateTimeOffset _lastReport = DateTimeOffset.MinValue;
        private string _lastStage = string.Empty;

        public JobProgressSink(IngestionJobState state, TimeSpan minimumInterval)
        {
            _state = state;
            _minimumInterval = minimumInterval;
        }

        public void Report(string stage, string detail, int? percentComplete = null, bool force = false)
        {
            var now = DateTimeOffset.UtcNow;
            var stageChanged = !string.Equals(_lastStage, stage, StringComparison.Ordinal);
            if (!force && !stageChanged && now - _lastReport < _minimumInterval)
            {
                return;
            }

            _lastStage = stage;
            _lastReport = now;
            _state.Update(stage, detail, percentComplete, force: true);
        }
    }
}

internal sealed class IngestionJobState
{
    private readonly object _gate = new();

    public IngestionJobState(Guid jobId, string kind, string title, Guid sourceId, string? sourceName)
    {
        JobId = jobId;
        Kind = kind;
        Title = title;
        SourceId = sourceId;
        SourceName = sourceName;
        CreatedAt = DateTimeOffset.UtcNow;
        LastHeartbeatAt = CreatedAt;
    }

    public Guid JobId { get; }
    public string Kind { get; }
    public string Title { get; }
    public Guid SourceId { get; }
    public string? SourceName { get; }
    public DateTimeOffset CreatedAt { get; }
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return Status is "Queued" or "Running";
            }
        }
    }

    private string Status { get; set; } = "Queued";
    private string Stage { get; set; } = "Queued";
    private int PercentComplete { get; set; }
    private int CompletedUnits { get; set; }
    private int TotalUnits { get; set; } = 100;
    private string Detail { get; set; } = "Waiting for the ingestion worker.";
    private DateTimeOffset? StartedAt { get; set; }
    private DateTimeOffset LastHeartbeatAt { get; set; }
    private DateTimeOffset? CompletedAt { get; set; }
    private string? Error { get; set; }

    public void Start(string stage, string detail, int percentComplete)
    {
        lock (_gate)
        {
            StartedAt = DateTimeOffset.UtcNow;
            Status = "Running";
            Apply(stage, detail, percentComplete);
        }
    }

    public void Update(string stage, string detail, int? percentComplete = null, bool force = false)
    {
        lock (_gate)
        {
            if (Status != "Running")
            {
                return;
            }

            Apply(stage, detail, percentComplete);
        }
    }

    public void UpdateUnits(string stage, string detail, int completedUnits, int totalUnits)
    {
        lock (_gate)
        {
            if (Status != "Running")
            {
                return;
            }

            TotalUnits = Math.Max(totalUnits, 1);
            CompletedUnits = Math.Clamp(completedUnits, 0, TotalUnits);
            PercentComplete = (int)Math.Round(CompletedUnits / (double)TotalUnits * 100d);
            Stage = string.IsNullOrWhiteSpace(stage) ? Stage : stage;
            Detail = string.IsNullOrWhiteSpace(detail) ? Detail : detail;
            LastHeartbeatAt = DateTimeOffset.UtcNow;
        }
    }

    public void Succeed(string stage, string detail)
    {
        lock (_gate)
        {
            Status = "Succeeded";
            Apply(stage, detail, 100);
            CompletedAt = DateTimeOffset.UtcNow;
            Error = null;
        }
    }

    public void Fail(string stage, string error)
    {
        lock (_gate)
        {
            Status = "Failed";
            Apply(stage, error, Math.Max(PercentComplete, 1));
            CompletedAt = DateTimeOffset.UtcNow;
            Error = error;
        }
    }

    public void Cancel(string detail)
    {
        lock (_gate)
        {
            Status = "Cancelled";
            Apply("Cancelled", detail, PercentComplete);
            CompletedAt = DateTimeOffset.UtcNow;
            Error = detail;
        }
    }

    public IngestionJobSnapshot ToSnapshot()
    {
        lock (_gate)
        {
            return new IngestionJobSnapshot(
                JobId,
                Kind,
                Title,
                Status,
                Stage,
                PercentComplete,
                CompletedUnits,
                TotalUnits,
                Detail,
                SourceId,
                SourceName,
                CreatedAt,
                StartedAt,
                LastHeartbeatAt,
                CompletedAt,
                Error);
        }
    }

    private void Apply(string stage, string detail, int? percentComplete)
    {
        Stage = string.IsNullOrWhiteSpace(stage) ? Stage : stage;
        Detail = string.IsNullOrWhiteSpace(detail) ? Detail : detail;
        if (percentComplete.HasValue)
        {
            PercentComplete = Math.Clamp(percentComplete.Value, 0, 100);
            CompletedUnits = PercentComplete;
        }

        LastHeartbeatAt = DateTimeOffset.UtcNow;
    }
}

internal sealed record IngestionJobWorkItem(
    Guid JobId,
    string Kind,
    string Title,
    Guid SourceId,
    string? SourceName,
    IngestionJobEngine Engine,
    string? Content,
    WikiSearchRequest? WikiRequest,
    string? RepairQuery,
    string? ContentType,
    string? TempFilePath,
    long SizeBytes)
{
    public static IngestionJobWorkItem ForUploadedFile(
        Guid sourceId,
        string sourceName,
        string contentType,
        string tempFilePath,
        long sizeBytes)
    {
        return new IngestionJobWorkItem(
            Guid.NewGuid(),
            "UploadIngestion",
            sourceName,
            sourceId,
            sourceName,
            IngestionJobEngine.Rags,
            null,
            null,
            null,
            contentType,
            tempFilePath,
            sizeBytes);
    }

    public static IngestionJobWorkItem ForContent(
        IngestionJobEngine engine,
        Guid sourceId,
        string content,
        string? sourceName)
    {
        var title = string.IsNullOrWhiteSpace(sourceName)
            ? $"{engine} content {sourceId:N}"[..Math.Min($"{engine} content {sourceId:N}".Length, 48)]
            : sourceName;

        return new IngestionJobWorkItem(
            Guid.NewGuid(),
            $"{engine}Ingestion",
            title,
            sourceId,
            sourceName,
            engine,
            content,
            null,
            null,
            null,
            null,
            content.Length);
    }

    public static IngestionJobWorkItem ForWikiRegeneration(WikiSearchRequest request)
    {
        var normalized = new WikiSearchRequest
        {
            Query = request.Query.Trim(),
            Mode = string.IsNullOrWhiteSpace(request.Mode) ? "wrags" : request.Mode,
            TopK = Math.Clamp(request.TopK, 1, 12),
            Expansion = Math.Clamp(request.Expansion, 0, 3),
            Regenerate = true
        };
        var title = $"WRAGS regeneration: {normalized.Query}";
        if (title.Length > 72)
        {
            title = title[..72];
        }

        return new IngestionJobWorkItem(
            Guid.NewGuid(),
            "WikiRegeneration",
            title,
            Guid.NewGuid(),
            normalized.Query,
            IngestionJobEngine.WikiRegeneration,
            null,
            normalized,
            null,
            null,
            null,
            normalized.Query.Length);
    }

    public static IngestionJobWorkItem ForRagsRepair(string? query)
    {
        var normalizedQuery = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var title = string.IsNullOrWhiteSpace(normalizedQuery)
            ? "RAGS index repair"
            : $"RAGS index repair: {normalizedQuery}";
        if (title.Length > 72)
        {
            title = title[..72];
        }

        return new IngestionJobWorkItem(
            Guid.NewGuid(),
            "RagsRepair",
            title,
            Guid.Empty,
            normalizedQuery,
            IngestionJobEngine.RagsRepair,
            null,
            null,
            normalizedQuery,
            null,
            null,
            normalizedQuery?.Length ?? 0);
    }

    public static IngestionJobWorkItem ForDocumentBriefs(Guid? sourceId, string? sourceName)
    {
        var isSingle = sourceId.HasValue && sourceId.Value != Guid.Empty;
        var normalizedName = string.IsNullOrWhiteSpace(sourceName) ? null : sourceName.Trim();
        var title = isSingle
            ? $"Document brief: {normalizedName ?? sourceId!.Value.ToString("N")}"
            : "Document briefs for all registered documents";
        if (title.Length > 72)
        {
            title = title[..72];
        }

        return new IngestionJobWorkItem(
            Guid.NewGuid(),
            "DocumentBriefs",
            title,
            sourceId ?? Guid.Empty,
            normalizedName,
            IngestionJobEngine.DocumentBriefs,
            null,
            null,
            null,
            null,
            null,
            0);
    }
}
