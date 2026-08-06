using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.DocumentBriefs;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;

namespace RAGS.UnitTests.DocumentBriefs;

public sealed class DocumentBriefServiceTests
{
    private static readonly Guid SourceId = Guid.NewGuid();
    private const string SourceName = "CMP 2026 - 3. RFP Analysis.docx";
    private const string CanonicalName = "3.0 - RFP Analysis";

    [Fact]
    public async Task RegenerateAsync_writes_document_brief_from_template_and_evidence()
    {
        var template = new FakeTemplateRegistry(
            new[] { new DocumentTemplateSection("Scope", "Project scope."), new DocumentTemplateSection("Timeline", "Delivery schedule.") });
        var rags = new FakeRagsService(SourceId);
        var repository = new CapturingWikiPageRepository();
        var generator = new FixedBriefGenerator("This document defines the Cleveland Metroparks CMP effort.");
        var service = CreateService(template, rags, repository, generator);

        var result = await service.RegenerateAsync(SourceId, SourceName);

        Assert.True(result.IsSuccess);
        Assert.Single(repository.SavedPages);
        var page = repository.SavedPages[0];
        Assert.Equal("document-brief", page.GeneratedFrom);
        Assert.Equal(SourceId, page.PrimarySourceId);
        Assert.Equal(new[] { SourceId }, page.SourceIds);
        Assert.Equal(CanonicalName, page.Topic);
        Assert.Equal(SourceName, page.Title);
        Assert.Equal("This document defines the Cleveland Metroparks CMP effort.", page.Summary);
        Assert.Equal("document-brief", page.RetrievalStrategy);
        Assert.NotEmpty(page.Citations);
        Assert.Equal(1, rags.OpeningChunkCalls);
        Assert.Equal(2, rags.SectionRetrievalCalls);
        Assert.NotNull(generator.LastRequest);
        Assert.Equal(CanonicalName, generator.LastRequest!.CanonicalName);
        Assert.Equal(2, generator.LastRequest.Sections.Count);
        Assert.Equal("Scope", generator.LastRequest.Sections[0].Title);
        Assert.True(generator.LastRequest.Evidence.Count >= 3, "Opening chunks plus per-section evidence should be present.");
    }

    [Fact]
    public async Task RegenerateAsync_fails_when_no_canonical_template_matches()
    {
        var service = CreateService(
            new FakeTemplateRegistry(Array.Empty<DocumentTemplateSection>(), "3.0 - RFP Analysis"),
            new FakeRagsService(SourceId),
            new CapturingWikiPageRepository(),
            new FixedBriefGenerator("brief"));

        var result = await service.RegenerateAsync(SourceId, "Q3 Financial Report.xlsx");

        Assert.True(result.IsFailure);
        Assert.Contains("canonical", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegenerateAllAsync_generates_brief_for_each_registered_document()
    {
        var template = new FakeTemplateRegistry(
            new[] { new DocumentTemplateSection("Scope", "Project scope.") });
        var rags = new FakeRagsService(SourceId);
        var repository = new CapturingWikiPageRepository();
        var generator = new FixedBriefGenerator("brief");
        var metadata = new FakeMetadataRepository(SourceId, SourceName);
        var service = CreateService(template, rags, repository, generator, metadata);

        var result = await service.RegenerateAllAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalDocuments);
        Assert.Equal(1, result.Value.Generated);
        Assert.Empty(result.Value.Skipped);
        Assert.Single(repository.SavedPages);
        Assert.Equal("document-brief", repository.SavedPages[0].GeneratedFrom);
    }

    private static DocumentBriefService CreateService(
        IDocumentTemplateRegistry template,
        IRagsService rags,
        IWikiPageRepository repository,
        IDocumentBriefGenerator generator,
        IMetadataRepository? metadata = null)
    {
        return new DocumentBriefService(
            metadata ?? new FakeMetadataRepository(SourceId, SourceName),
            template,
            rags,
            repository,
            generator);
    }

    private sealed class FixedBriefGenerator : IDocumentBriefGenerator
    {
        public FixedBriefGenerator(string brief) => Brief = brief;

        public string Brief { get; }

        public DocumentBriefRequest? LastRequest { get; private set; }

        public Task<Result<string>> GenerateAsync(DocumentBriefRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Result<string>.Success(Brief));
        }
    }

    private sealed class FakeTemplateRegistry : IDocumentTemplateRegistry
    {
        private readonly IReadOnlyList<DocumentTemplateSection> _sections;
        private readonly string? _canonical;

        public FakeTemplateRegistry(IReadOnlyList<DocumentTemplateSection> sections, string? canonical = CanonicalName)
        {
            _sections = sections;
            _canonical = canonical;
        }

        public IReadOnlyList<DocumentTemplateSection>? TryGetSections(string fileName)
        {
            return fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) && _canonical is not null ? _sections : null;
        }

        public string? TryGetCanonicalName(string fileName)
        {
            return fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) && _canonical is not null ? _canonical : null;
        }

        public string? TryGetTheme(string fileName)
        {
            return TryGetCanonicalName(fileName) is null ? null : "Analysis";
        }

        public IReadOnlyList<string> ListThemes()
        {
            return new[] { "Analysis" };
        }
    }

    private sealed class FakeRagsService : IRagsService
    {
        private readonly Guid _sourceId;

        public FakeRagsService(Guid sourceId) => _sourceId = sourceId;

        public int OpeningChunkCalls { get; private set; }

        public int SectionRetrievalCalls { get; private set; }

        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
        {
            SectionRetrievalCalls++;
            IReadOnlyList<SearchResult> results = new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), request.SourceId ?? _sourceId, $"{request.Query} evidence chunk.", 1),
                    0.9f,
                    new[] { "CMP 2026 - 3. RFP Analysis.docx" },
                    retrievalStrategy: "semantic",
                    rank: 1)
            };
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(results));
        }

        public async Task<Result<IReadOnlyList<SearchResult>>> RetrieveSourceChunksAsync(Guid sourceId, int take, CancellationToken cancellationToken = default)
        {
            OpeningChunkCalls++;
            await Task.CompletedTask;
            IReadOnlyList<SearchResult> results = new[]
            {
                new SearchResult(
                    new Chunk(Guid.NewGuid(), sourceId, "Project Summary: Cleveland Metroparks CMP is a comprehensive planning effort.", 0),
                    1f,
                    new[] { "CMP 2026 - 3. RFP Analysis.docx" },
                    retrievalStrategy: "semantic",
                    rank: 1)
            };
            return Result<IReadOnlyList<SearchResult>>.Success(results);
        }
    }

    private sealed class CapturingWikiPageRepository : IWikiPageRepository
    {
        public List<WikiPage> SavedPages { get; } = new();

        public Task<Result<IReadOnlyList<WikiPage>>> SearchAsync(string query, int topK, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<WikiPage>>.Success(SavedPages.ToList()));
        }

        public Task<Result<IReadOnlyList<WikiPage>>> GetRecentAsync(int take, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<WikiPage>>.Success(SavedPages.Take(take).ToList()));
        }

        public Task<Result<WikiPage?>> GetAsync(Guid pageId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<WikiPage?>.Success(SavedPages.FirstOrDefault(p => p.Id == pageId)));
        }

        public Task<Result<IReadOnlyList<WikiPageLink>>> GetRelatedAsync(Guid pageId, int take, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<WikiPageLink> links = Array.Empty<WikiPageLink>();
            return Task.FromResult(Result<IReadOnlyList<WikiPageLink>>.Success(links));
        }

        public Task<Result<IReadOnlyList<WikiPageHistoryEntry>>> GetHistoryAsync(Guid pageId, int take, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<WikiPageHistoryEntry> history = Array.Empty<WikiPageHistoryEntry>();
            return Task.FromResult(Result<IReadOnlyList<WikiPageHistoryEntry>>.Success(history));
        }

        public Task<Result<IReadOnlyList<WikiPage>>> UpsertAsync(IReadOnlyList<WikiPage> pages, CancellationToken cancellationToken = default)
        {
            SavedPages.AddRange(pages);
            return Task.FromResult(Result<IReadOnlyList<WikiPage>>.Success(pages));
        }

        public Task<Result<WikiPage?>> UpdateStatusAsync(Guid pageId, string status, string? reviewedBy, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<WikiPage?>.Success(null));
        }

        public Task<Result<WikiPage?>> UpdatePageAsync(Guid pageId, WikiPageEditRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<WikiPage?>.Success(null));
        }
    }

    private sealed class FakeMetadataRepository : IMetadataRepository
    {
        private readonly Guid _sourceId;
        private readonly string _sourceName;

        public FakeMetadataRepository(Guid sourceId, string sourceName)
        {
            _sourceId = sourceId;
            _sourceName = sourceName;
        }

        public Task<Result<FileMetadata>> GetAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<FileMetadata>.Failure("Not used."));
        }

        public Task<Result<FileMetadata>> SaveAsync(FileMetadata metadata, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<FileMetadata>.Success(metadata));
        }

        public Task<Result<PagedResult<FileMetadata>>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            var metadata = new FileMetadata(
                new FileDescriptor(_sourceId, _sourceName),
                "application/octet-stream",
                100,
                DateTimeOffset.UtcNow);
            var page = new PagedResult<FileMetadata>(new[] { metadata }, 1, 100, 1);
            return Task.FromResult(Result<PagedResult<FileMetadata>>.Success(page));
        }

        public Task<Result> DeleteAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }
    }
}
