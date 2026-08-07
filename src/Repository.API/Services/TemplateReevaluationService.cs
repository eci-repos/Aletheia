using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Application;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aletheia.Repository.API.Services;

/// <summary>
/// Sprint 59: re-resolves the canonical template (name + themes + status) for documents that are
/// not Canonical (null or Uncategorized template status) and persists the result. Doubles as the
/// backfill for pre-Sprint-58 rows (null template_name/theme) and as the promotion trigger that
/// generates a document brief once a template matches an uncategorized document.
/// </summary>
public sealed class TemplateReevaluationService
{
    private readonly IMetadataRepository? _metadataRepository;
    private readonly IDocumentTemplateRegistry? _templateRegistry;
    private readonly Lazy<IIngestionJobService>? _ingestionJobs;
    private readonly ILogger<TemplateReevaluationService> _logger;

    public TemplateReevaluationService(
        IMetadataRepository? metadataRepository = null,
        IDocumentTemplateRegistry? templateRegistry = null,
        Lazy<IIngestionJobService>? ingestionJobs = null,
        ILogger<TemplateReevaluationService>? logger = null)
    {
        _metadataRepository = metadataRepository;
        _templateRegistry = templateRegistry;
        _ingestionJobs = ingestionJobs;
        _logger = logger ?? NullLogger<TemplateReevaluationService>.Instance;
    }

    /// <summary>Re-evaluates one document (sourceId) or all non-Canonical documents, persisting template name, themes, and status.</summary>
    public async Task<Result<TemplateReevaluationSummary>> ReevaluateAsync(
        Guid? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        if (_metadataRepository is null)
        {
            return Result<TemplateReevaluationSummary>.Failure("Metadata repository is not available.");
        }

        var rowsResult = await _metadataRepository
            .ListUncategorizedAsync(cancellationToken)
            .ConfigureAwait(false);
        if (rowsResult.IsFailure)
        {
            return Result<TemplateReevaluationSummary>.Failure(rowsResult.Error ?? "Unable to list uncategorized documents.");
        }

        var rows = rowsResult.Value ?? new List<FileThemeRow>();
        if (sourceId.HasValue)
        {
            rows = rows.Where(row => row.FileId == sourceId.Value).ToList();
        }

        var evaluated = 0;
        var promoted = 0;
        var uncategorized = 0;

        foreach (var row in rows)
        {
            var canonicalName = _templateRegistry?.TryGetCanonicalName(row.FileName);
            var themes = _templateRegistry?.TryGetThemes(row.FileName);
            var status = canonicalName is null ? KnowledgeThemeService.Uncategorized : KnowledgeThemeService.Canonical;

            var result = await _metadataRepository
                .SetTemplateAsync(row.FileId, canonicalName, themes, status, cancellationToken)
                .ConfigureAwait(false);
            if (result.IsFailure)
            {
                _logger.LogWarning("Template re-evaluation failed for {SourceId}: {Error}.", row.FileId, result.Error);
                continue;
            }

            evaluated++;
            if (status == KnowledgeThemeService.Canonical)
            {
                promoted++;
                EnqueueBrief(row);
            }
            else
            {
                uncategorized++;
            }
        }

        _logger.LogInformation(
            "Template re-evaluation completed: {Evaluated} evaluated, {Promoted} promoted, {Uncategorized} uncategorized.",
            evaluated,
            promoted,
            uncategorized);
        return Result<TemplateReevaluationSummary>.Success(new TemplateReevaluationSummary(evaluated, promoted, uncategorized));
    }

    private void EnqueueBrief(FileThemeRow row)
    {
        try
        {
            _ingestionJobs?.Value.EnqueueDocumentBriefs(row.FileId, row.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to queue document brief generation for {SourceName}.", row.FileName);
        }
    }
}

/// <summary>Result of a template re-evaluation pass.</summary>
public sealed record TemplateReevaluationSummary(int Evaluated, int Promoted, int Uncategorized);
