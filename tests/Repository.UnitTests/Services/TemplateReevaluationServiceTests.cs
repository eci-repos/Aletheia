using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Application;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.API.Services;
using Moq;

namespace Repository.UnitTests.Services;

/// <summary>
/// Guards Sprint 59 re-evaluation: re-resolves the canonical template (name + themes + status) for
/// non-Canonical documents, doubles as the backfill for pre-Sprint-58 rows, and generates a document
/// brief when a document is promoted to Canonical.
/// </summary>
public sealed class TemplateReevaluationServiceTests
{
    [Fact]
    public async Task ReevaluateAsync_promotes_uncategorized_to_canonical_and_enqueues_brief()
    {
        var uncategorizedId = Guid.NewGuid();
        var repository = new FakeMetadataRepository(new List<FileThemeRow>
        {
            new(uncategorizedId, "Q3 Financial Report.xlsx", null, null, "Uncategorized"),
            new(Guid.NewGuid(), "CMP 2026 - 3. RFP Analysis.docx", null, null, "Uncategorized")
        });
        var jobs = new Mock<IIngestionJobService>();
        var service = new TemplateReevaluationService(
            repository,
            new DocumentTemplateRegistry(),
            new Lazy<IIngestionJobService>(() => jobs.Object));

        var result = await service.ReevaluateAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Evaluated);
        Assert.Equal(1, result.Value.Promoted);
        Assert.Equal(1, result.Value.Uncategorized);
        jobs.Verify(j => j.EnqueueDocumentBriefs(It.IsAny<Guid>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ReevaluateAsync_backfills_pre_migration_rows()
    {
        var fileId = Guid.NewGuid();
        var repository = new FakeMetadataRepository(new List<FileThemeRow>
        {
            new(fileId, "CMP 2022 - 3. RFP Analysis.docx", null, null, null)
        });
        var service = new TemplateReevaluationService(repository, new DocumentTemplateRegistry());

        var result = await service.ReevaluateAsync();

        Assert.True(result.IsSuccess);
        var updated = repository.LastSetTemplate;
        Assert.NotNull(updated);
        Assert.Equal("3.0 - RFP Analysis", updated!.Value.TemplateName);
        Assert.Equal("Canonical", updated.Value.TemplateStatus);
        Assert.Contains("Analysis", updated.Value.Themes!);
    }

    [Fact]
    public async Task ReevaluateAsync_filters_to_single_source()
    {
        var targetId = Guid.NewGuid();
        var repository = new FakeMetadataRepository(new List<FileThemeRow>
        {
            new(targetId, "Q3 Financial Report.xlsx", null, null, "Uncategorized"),
            new(Guid.NewGuid(), "Budget Notes.xlsx", null, null, "Uncategorized")
        });
        var service = new TemplateReevaluationService(repository, new DocumentTemplateRegistry());

        var result = await service.ReevaluateAsync(targetId);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Evaluated);
        Assert.Equal(targetId, repository.LastSetTemplate!.Value.FileId);
    }

    private sealed class FakeMetadataRepository : IMetadataRepository
    {
        private readonly IReadOnlyList<FileThemeRow> _rows;

        public (Guid FileId, string? TemplateName, IReadOnlyList<string>? Themes, string? TemplateStatus)? LastSetTemplate { get; private set; }

        public FakeMetadataRepository(IReadOnlyList<FileThemeRow> rows)
        {
            _rows = rows;
        }

        public Task<Result<IReadOnlyList<FileThemeRow>>> ListUncategorizedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<FileThemeRow>>.Success(_rows));

        public Task<Result> SetTemplateAsync(
            Guid fileId,
            string? templateName,
            IReadOnlyList<string>? themes,
            string? templateStatus = null,
            CancellationToken cancellationToken = default)
        {
            LastSetTemplate = (fileId, templateName, themes, templateStatus);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<FileMetadata>> GetAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<FileMetadata>.Failure("not used"));

        public Task<Result<FileMetadata>> SaveAsync(FileMetadata metadata, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<FileMetadata>.Success(metadata));

        public Task<Result<PagedResult<FileMetadata>>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<PagedResult<FileMetadata>>.Success(new PagedResult<FileMetadata>(new List<FileMetadata>(), 1, 10, 0)));

        public Task<Result> DeleteAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());
    }
}
