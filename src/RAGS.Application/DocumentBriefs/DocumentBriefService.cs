using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aletheia.RAGS.Application.DocumentBriefs;

/// <summary>
/// Generates durable, end-user-readable "document briefs" for registered documents.
/// A brief opens with the document's stated nature/purpose (opening chunks), then covers
/// the canonical template's ordered sections in order, each grounded in per-section
/// retrieved evidence, cited, and written in plain language. Briefs are stored as
/// <c>wiki_pages</c> rows with <c>generated_from = 'document-brief'</c>.
/// </summary>
public sealed class DocumentBriefService : IDocumentBriefService
{
    private const int OpeningChunkTake = 3;
    private const int SectionEvidenceTopK = 3;
    private const int MaxRelatedTopics = 8;

    private readonly IMetadataRepository _metadataRepository;
    private readonly IDocumentTemplateRegistry _templateRegistry;
    private readonly IRagsService _ragsService;
    private readonly IWikiPageRepository _wikiRepository;
    private readonly IDocumentBriefGenerator _generator;
    private readonly ILogger<DocumentBriefService> _logger;

    public DocumentBriefService(
        IMetadataRepository metadataRepository,
        IDocumentTemplateRegistry templateRegistry,
        IRagsService ragsService,
        IWikiPageRepository wikiRepository,
        IDocumentBriefGenerator generator,
        ILogger<DocumentBriefService>? logger = null)
    {
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
        _templateRegistry = templateRegistry ?? throw new ArgumentNullException(nameof(templateRegistry));
        _ragsService = ragsService ?? throw new ArgumentNullException(nameof(ragsService));
        _wikiRepository = wikiRepository ?? throw new ArgumentNullException(nameof(wikiRepository));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _logger = logger ?? NullLogger<DocumentBriefService>.Instance;
    }

    public async Task<Result<WikiPage>> RegenerateAsync(
        Guid sourceId,
        string sourceName,
        CancellationToken cancellationToken = default)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Source ID is required.", nameof(sourceId));
        }

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("Source name is required.", nameof(sourceName));
        }

        var canonicalName = _templateRegistry.TryGetCanonicalName(sourceName);
        if (canonicalName is null)
        {
            _logger.LogWarning(
                "Document brief skipped for {SourceName}: no canonical document template found.",
                sourceName);
            return Result<WikiPage>.Failure(
                $"Document brief skipped for '{sourceName}': no canonical document template found.");
        }

        var sections = _templateRegistry.TryGetSections(sourceName) ?? Array.Empty<DocumentTemplateSection>();
        var evidence = await CollectEvidenceAsync(sourceId, sourceName, sections, cancellationToken).ConfigureAwait(false);
        if (evidence.Count == 0)
        {
            _logger.LogWarning(
                "Document brief skipped for {SourceName}: no retrieved evidence is available.",
                sourceName);
            return Result<WikiPage>.Failure(
                $"Document brief skipped for '{sourceName}': no retrieved evidence is available.");
        }

        var request = new DocumentBriefRequest
        {
            SourceId = sourceId,
            SourceName = sourceName,
            CanonicalName = canonicalName,
            Sections = sections,
            Evidence = evidence
        };

        var generated = await _generator.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        if (generated.IsFailure || string.IsNullOrWhiteSpace(generated.Value))
        {
            return Result<WikiPage>.Failure(generated.Error ?? $"Document brief generation failed for '{sourceName}'.");
        }

        var page = CreatePage(request, generated.Value);
        var saved = await _wikiRepository.UpsertAsync(new[] { page }, cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
        {
            return Result<WikiPage>.Failure(saved.Error ?? "Document brief persistence failed.");
        }

        _logger.LogInformation(
            "Document brief generated for {SourceName} ({SourceId}) from canonical '{CanonicalName}' with {SectionCount} section(s) and {EvidenceCount} evidence item(s).",
            sourceName,
            sourceId,
            canonicalName,
            sections.Count,
            evidence.Count);

        return Result<WikiPage>.Success(saved.Value is { Count: > 0 } ? saved.Value[0] : page);
    }

    public async Task<Result<DocumentBriefRegenerationResult>> RegenerateAllAsync(
        Action<DocumentBriefProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sources = await LoadRegisteredSourcesAsync(cancellationToken).ConfigureAwait(false);
        if (sources.Count == 0)
        {
            return Result<DocumentBriefRegenerationResult>.Success(
                new DocumentBriefRegenerationResult(0, 0, Array.Empty<string>()));
        }

        var skipped = new List<string>();
        var generated = 0;
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            progress?.Invoke(new DocumentBriefProgress(
                "Document briefs",
                $"Generating brief for {source.SourceName} ({i + 1} of {sources.Count}).",
                i,
                sources.Count));

            var result = await RegenerateAsync(source.SourceId, source.SourceName, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                generated++;
            }
            else
            {
                skipped.Add($"{source.SourceName}: {result.Error}");
            }

            progress?.Invoke(new DocumentBriefProgress(
                "Document briefs",
                $"Completed {i + 1} of {sources.Count} document brief(s).",
                i + 1,
                sources.Count));
        }

        return Result<DocumentBriefRegenerationResult>.Success(
            new DocumentBriefRegenerationResult(sources.Count, generated, skipped));
    }

    private async Task<IReadOnlyList<SearchResult>> CollectEvidenceAsync(
        Guid sourceId,
        string sourceName,
        IReadOnlyList<DocumentTemplateSection> sections,
        CancellationToken cancellationToken)
    {
        var evidence = new List<SearchResult>();
        var seenChunkIds = new HashSet<Guid>();

        // Deterministic opening-chunk injection: the document's nature/purpose comes first.
        var opening = await _ragsService
            .RetrieveSourceChunksAsync(sourceId, OpeningChunkTake, cancellationToken)
            .ConfigureAwait(false);
        if (opening.IsSuccess && opening.Value is { Count: > 0 })
        {
            AddUnique(evidence, seenChunkIds, opening.Value);
        }

        foreach (var section in sections)
        {
            var query = BuildSectionQuery(section);
            var result = await _ragsService
                .RetrieveAsync(new RetrievalRequest(query, SectionEvidenceTopK, sourceId), cancellationToken)
                .ConfigureAwait(false);
            if (result.IsSuccess && result.Value is { Count: > 0 })
            {
                AddUnique(evidence, seenChunkIds, result.Value);
            }
        }

        _logger.LogInformation(
            "Document brief evidence collected for {SourceName}: {Count} chunk(s) across {SectionCount} template section(s).",
            sourceName,
            evidence.Count,
            sections.Count);

        return evidence;
    }

    private static void AddUnique(List<SearchResult> evidence, HashSet<Guid> seenChunkIds, IReadOnlyList<SearchResult> results)
    {
        foreach (var result in results)
        {
            if (seenChunkIds.Add(result.Chunk.Id))
            {
                evidence.Add(result);
            }
        }
    }

    private static string BuildSectionQuery(DocumentTemplateSection section)
    {
        if (string.IsNullOrWhiteSpace(section.Description))
        {
            return section.Title.Trim();
        }

        return $"{section.Title.Trim()} - {section.Description.Trim()}";
    }

    private static WikiPage CreatePage(DocumentBriefRequest request, string brief)
    {
        var citations = request.Evidence
            .SelectMany(result => result.Citations)
            .Where(citation => !string.IsNullOrWhiteSpace(citation))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        var now = DateTimeOffset.UtcNow;
        return new WikiPage(
            Guid.NewGuid(),
            request.CanonicalName,
            request.SourceName,
            brief,
            new[] { request.SourceId },
            citations,
            "document-brief",
            version: 1,
            status: "Generated",
            score: 1f,
            rank: 1,
            retrievalStrategy: "document-brief",
            primarySourceId: request.SourceId,
            chunkIndex: null,
            relatedTopics: ExtractRelatedTopics(request.SourceName, brief),
            createdAt: now,
            updatedAt: now);
    }

    private static IReadOnlyList<string> ExtractRelatedTopics(string sourceName, string brief)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in $"{sourceName} {brief}".Split(new[] { ' ', '\r', '\n', '\t', ',', '.', ';', ':', '(', ')', '[', ']', '{', '}', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (values.Count >= MaxRelatedTopics)
            {
                break;
            }

            var cleaned = token.Trim('-', '#', '*', '/', '\\');
            if (cleaned.Length < 4 || cleaned.Length > 48)
            {
                continue;
            }

            if (char.IsUpper(cleaned[0]) || cleaned.Contains('-', StringComparison.Ordinal))
            {
                values.Add(cleaned.Length <= 64 ? cleaned : cleaned[..64]);
            }
        }

        return values.Take(MaxRelatedTopics).ToList();
    }

    private async Task<IReadOnlyList<KnowledgeSource>> LoadRegisteredSourcesAsync(CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var page = 1;
        var sources = new List<KnowledgeSource>();

        while (true)
        {
            var result = await _metadataRepository
                .SearchAsync(new SearchRequest(null, page, pageSize), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure || result.Value is null)
            {
                return Array.Empty<KnowledgeSource>();
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

        return sources
            .GroupBy(source => source.SourceId)
            .Select(group => group.OrderByDescending(source => source.UploadedAt).First())
            .OrderBy(source => source.SourceName)
            .ToList();
    }
}
