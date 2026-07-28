using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Domain.UseCases;

namespace Aletheia.Repository.API.Services;

public sealed class RepositoryKnowledgeSourceIngestionService : IKnowledgeSourceIngestionService
{
    private readonly IDownloadUseCase _downloadUseCase;
    private readonly IUploadedFileTextExtractor _textExtractor;
    private readonly IRagsService _ragsService;
    private readonly IUploadedContentKnowledgeIndexer _knowledgeIndexer;

    public RepositoryKnowledgeSourceIngestionService(
        IDownloadUseCase downloadUseCase,
        IUploadedFileTextExtractor textExtractor,
        IRagsService ragsService,
        IUploadedContentKnowledgeIndexer knowledgeIndexer)
    {
        _downloadUseCase = downloadUseCase ?? throw new ArgumentNullException(nameof(downloadUseCase));
        _textExtractor = textExtractor ?? throw new ArgumentNullException(nameof(textExtractor));
        _ragsService = ragsService ?? throw new ArgumentNullException(nameof(ragsService));
        _knowledgeIndexer = knowledgeIndexer ?? throw new ArgumentNullException(nameof(knowledgeIndexer));
    }

    public async Task<Result<bool>> EnsureIngestedAsync(
        KnowledgeSource source,
        CancellationToken cancellationToken = default)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var download = await _downloadUseCase
            .DownloadAsync(new DownloadRequest(new FileDescriptor(source.SourceId, source.SourceName)), cancellationToken)
            .ConfigureAwait(false);

        if (download.IsFailure || download.Value is null)
        {
            return Result<bool>.Failure(download.Error ?? "Knowledge source download failed.");
        }

        using var content = download.Value.Content;
        var extraction = await _textExtractor
            .ExtractAsync(source.SourceName, download.Value.Metadata.ContentType, content, cancellationToken)
            .ConfigureAwait(false);

        if (extraction.IsFailure || extraction.Value is null)
        {
            return Result<bool>.Failure(extraction.Error ?? "Knowledge source text extraction failed.");
        }

        if (!extraction.Value.IsSupported || string.IsNullOrWhiteSpace(extraction.Value.Text))
        {
            return Result<bool>.Success(false);
        }

        var ingestion = await _ragsService
            .IngestAsync(new IngestionRequest(source.SourceId, extraction.Value.Text, source.SourceName), cancellationToken)
            .ConfigureAwait(false);

        if (ingestion.IsFailure)
        {
            return Result<bool>.Failure(ingestion.Error ?? "Knowledge source RAGS ingestion failed.");
        }

        await _knowledgeIndexer
            .IndexAsync(source.SourceId, source.SourceName, extraction.Value.Text, cancellationToken)
            .ConfigureAwait(false);

        return Result<bool>.Success(true);
    }
}
