using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Domain.UseCases;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aletheia.Repository.API.Services;

public sealed class RepositoryKnowledgeSourceIngestionService : IKnowledgeSourceIngestionService
{
    private readonly IDownloadUseCase _downloadUseCase;
    private readonly IUploadedFileTextExtractor _textExtractor;
    private readonly IRagsService _ragsService;
    private readonly IUploadedContentKnowledgeIndexer _knowledgeIndexer;
    private readonly IDocumentTemplateRegistry? _templateRegistry;
    private readonly IGraphProvider? _graphProvider;
    private readonly IIngestionDiagnostics? _diagnostics;
    private readonly Lazy<IIngestionJobService>? _ingestionJobs;
    private readonly IMetadataRepository? _metadataRepository;
    private readonly IFactExtractionService? _factExtractionService;
    private readonly ILogger<RepositoryKnowledgeSourceIngestionService> _logger;

    public RepositoryKnowledgeSourceIngestionService(
        IDownloadUseCase downloadUseCase,
        IUploadedFileTextExtractor textExtractor,
        IRagsService ragsService,
        IUploadedContentKnowledgeIndexer knowledgeIndexer,
        IDocumentTemplateRegistry? templateRegistry = null,
        IGraphProvider? graphProvider = null,
        IIngestionDiagnostics? diagnostics = null,
        IMetadataRepository? metadataRepository = null,
        ILogger<RepositoryKnowledgeSourceIngestionService>? logger = null,
        Lazy<IIngestionJobService>? ingestionJobs = null,
        IFactExtractionService? factExtractionService = null)
    {
        _downloadUseCase = downloadUseCase ?? throw new ArgumentNullException(nameof(downloadUseCase));
        _textExtractor = textExtractor ?? throw new ArgumentNullException(nameof(textExtractor));
        _ragsService = ragsService ?? throw new ArgumentNullException(nameof(ragsService));
        _knowledgeIndexer = knowledgeIndexer ?? throw new ArgumentNullException(nameof(knowledgeIndexer));
        _templateRegistry = templateRegistry;
        _graphProvider = graphProvider;
        _diagnostics = diagnostics;
        _metadataRepository = metadataRepository;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RepositoryKnowledgeSourceIngestionService>.Instance;
        _ingestionJobs = ingestionJobs;
        _factExtractionService = factExtractionService;
    }

    public async Task<Result<bool>> EnsureIngestedAsync(
        KnowledgeSource source,
        CancellationToken cancellationToken = default,
        KnowledgeIndexMode mode = KnowledgeIndexMode.Full)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        _logger.LogInformation("Knowledge source hydration started for {SourceName} ({SourceId}).", source.SourceName, source.SourceId);

        // Sprint 59: softened canonical gate. A document with no matching template is ingested anyway
        // (RAGS + knowledge index + graph seed) with template_status = Uncategorized, so a new document
        // kind arriving before its template is written is never lost. Template-dependent features
        // (document briefs, per-section retrieval, theme) stay gated on Canonical status.
        var canonicalName = _templateRegistry?.TryGetCanonicalName(source.SourceName);
        var templateStatus = canonicalName is null ? KnowledgeThemeService.Uncategorized : KnowledgeThemeService.Canonical;
        if (canonicalName is null)
        {
            _diagnostics?.RecordUncategorizedIngest(source.SourceName);
            _logger.LogInformation(
                "No canonical document template found for {SourceName}; ingesting as Uncategorized. Register a matching template under docs/doc-templates and re-evaluate to promote it.",
                source.SourceName);
        }
        else
        {
            _logger.LogInformation("Canonical document template {CanonicalName} matched for {SourceName}.", canonicalName, source.SourceName);
        }

        // Sprint 58/59: persist the canonical template + knowledge themes + template status so sessions can filter by theme.
        await PersistTemplateAsync(source.SourceId, canonicalName, _templateRegistry?.TryGetThemes(source.SourceName), templateStatus, cancellationToken)
            .ConfigureAwait(false);

        var download = await _downloadUseCase
            .DownloadAsync(new DownloadRequest(new FileDescriptor(source.SourceId, source.SourceName)), cancellationToken)
            .ConfigureAwait(false);

        if (download.IsFailure || download.Value is null)
        {
            _logger.LogWarning("Knowledge source download failed for {SourceName}: {Error}.", source.SourceName, download.Error);
            return Result<bool>.Failure(download.Error ?? "Knowledge source download failed.");
        }

        _logger.LogInformation("Knowledge source {SourceName} downloaded; extracting text.", source.SourceName);

        using var content = download.Value.Content;
        var extraction = await _textExtractor
            .ExtractAsync(source.SourceName, download.Value.Metadata.ContentType, content, cancellationToken)
            .ConfigureAwait(false);

        if (extraction.IsFailure || extraction.Value is null)
        {
            _logger.LogWarning("Knowledge source text extraction failed for {SourceName}: {Error}.", source.SourceName, extraction.Error);
            _diagnostics?.RecordExtractionFailure(source.SourceName, extraction.Error ?? "unknown");
            return Result<bool>.Failure(extraction.Error ?? "Knowledge source text extraction failed.");
        }

        if (!extraction.Value.IsSupported || string.IsNullOrWhiteSpace(extraction.Value.Text))
        {
            _logger.LogInformation("Knowledge source {SourceName} has no extractable text; marking as not ingestable.", source.SourceName);
            // Sprint 73: stamp the marker as an audit record ("checked, non-ingestable"). The startup
            // reconciliation sweep is embeddings-only, so a non-ingestable source is re-checked on the
            // next restart — cheap (download + extract, no LLM) and self-healing if it later becomes
            // extractable. The marker is not a sweep gate.
            await StampLastIngestedAsync(source.SourceId, cancellationToken).ConfigureAwait(false);
            return Result<bool>.Success(false);
        }

        _logger.LogInformation("Knowledge source {SourceName} text extracted ({TextLength} chars); running RAGS ingestion.", source.SourceName, extraction.Value.Text.Length);

        // Sprint 70: grounded fact extraction — propose (LLM) → verify (fidelity gate) → normalize
        // (lexicon) → persist. Best-effort enrichment: a failure never blocks ingestion.
        if (_factExtractionService is not null)
        {
            try
            {
                var factResult = await _factExtractionService
                    .ExtractAsync(source.SourceId, extraction.Value.Text, extraction.Value.Pages, canonicalName, cancellationToken)
                    .ConfigureAwait(false);
                if (factResult.IsFailure)
                {
                    _logger.LogWarning("Grounded fact extraction failed for {SourceName}: {Error}.", source.SourceName, factResult.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Grounded fact extraction failed for {SourceName}.", source.SourceName);
            }
        }

        // Sprint 56 replace semantics: clear prior knowledge-index rows and graph nodes for this
        // source so re-ingestion (document updates, repairs) replaces content instead of accumulating.
        var knowledgeCleanup = await _knowledgeIndexer
            .DeleteSourceAsync(source.SourceId, cancellationToken)
            .ConfigureAwait(false);
        if (knowledgeCleanup is not null && knowledgeCleanup.IsFailure)
        {
            _logger.LogWarning("Knowledge source cleanup failed for {SourceName}: {Error}.", source.SourceName, knowledgeCleanup.Error);
        }

        if (_graphProvider is not null)
        {
            var graphCleanup = await _graphProvider
                .DeleteSourceAsync(source.SourceId.ToString(), cancellationToken)
                .ConfigureAwait(false);
            if (graphCleanup is not null && graphCleanup.IsFailure)
            {
                _logger.LogWarning("Graph source cleanup failed for {SourceName}: {Error}.", source.SourceName, graphCleanup.Error);
            }
        }

        var ingestion = await _ragsService
            .IngestAsync(new IngestionRequest(source.SourceId, extraction.Value.Text, source.SourceName, extraction.Value.Pages), cancellationToken)
            .ConfigureAwait(false);

        if (ingestion.IsFailure)
        {
            _logger.LogWarning("Knowledge source RAGS ingestion failed for {SourceName}: {Error}.", source.SourceName, ingestion.Error);
            return Result<bool>.Failure(ingestion.Error ?? "Knowledge source RAGS ingestion failed.");
        }

        _logger.LogInformation(
            "Knowledge source {SourceName} RAGS ingestion completed; indexing content ({IndexMode} mode).",
            source.SourceName,
            mode);

        // Sprint 62: reembed uses the lightweight indexer (no LLM graph-intelligence calls) for
        // parity with file uploads; repair and chat hydration keep the full path so the graph is
        // fully derived for them.
        if (mode == KnowledgeIndexMode.Lightweight)
        {
            await _knowledgeIndexer
                .IndexLightweightAsync(source.SourceId, source.SourceName, extraction.Value.Text, null, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await _knowledgeIndexer
                .IndexAsync(source.SourceId, source.SourceName, extraction.Value.Text, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogInformation("Knowledge source hydration completed for {SourceName}.", source.SourceName);

        // Sprint 73: stamp completion so the startup reconciliation sweep knows this source was
        // checked. Set on success only — a failed ingest stays null and remains a repair candidate.
        await StampLastIngestedAsync(source.SourceId, cancellationToken).ConfigureAwait(false);

        // Sprint 55: keep the user-facing Wiki fresh with a document brief once a registered
        // document is ingested. Sprint 59: briefs are gated on Canonical status (uncategorized
        // documents have no template sections to structure a brief). The lazy reference avoids a
        // construction-time cycle with IngestionJobService, which depends on this service for repair/hydration.
        if (canonicalName is not null)
        {
            try
            {
                _ingestionJobs?.Value.EnqueueDocumentBriefs(source.SourceId, source.SourceName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to queue document brief generation for {SourceName}.", source.SourceName);
            }
        }

        return Result<bool>.Success(true);
    }

    private async Task StampLastIngestedAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (_metadataRepository is null)
        {
            return;
        }

        try
        {
            var result = await _metadataRepository
                .SetLastIngestedAtAsync(fileId, DateTimeOffset.UtcNow, cancellationToken)
                .ConfigureAwait(false);
            if (result.IsFailure)
            {
                _logger.LogWarning("Unable to stamp last_ingested_at for {SourceId}: {Error}.", fileId, result.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to stamp last_ingested_at for {SourceId}.", fileId);
        }
    }

    private async Task PersistTemplateAsync(
        Guid fileId,
        string? canonicalName,
        IReadOnlyList<string>? themes,
        string? templateStatus,
        CancellationToken cancellationToken)
    {
        if (_metadataRepository is null)
        {
            return;
        }

        try
        {
            var result = await _metadataRepository
                .SetTemplateAsync(fileId, canonicalName, themes, templateStatus, cancellationToken)
                .ConfigureAwait(false);
            if (result.IsFailure)
            {
                _logger.LogWarning("Unable to persist template/theme for {SourceId}: {Error}.", fileId, result.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to persist template/theme for {SourceId}.", fileId);
        }
    }
}
