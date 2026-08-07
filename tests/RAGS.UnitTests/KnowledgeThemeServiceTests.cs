using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;

namespace RAGS.UnitTests;

public sealed class KnowledgeThemeServiceTests
{
    [Fact]
    public async Task ResolveSourceIdsAsync_uses_persisted_theme()
    {
        var sourceId = Guid.NewGuid();
        var service = CreateService(new List<FileThemeRow>
        {
            new(sourceId, "CMP 2026 - 3. RFP Analysis.docx", "3.0 - RFP Analysis", new[] { "Analysis" })
        });

        var result = await service.ResolveSourceIdsAsync(new[] { "Analysis" });

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { sourceId }, result.Value);
    }

    [Fact]
    public async Task ResolveSourceIdsAsync_falls_back_to_registry_derived_theme()
    {
        var sourceId = Guid.NewGuid();
        // Theme column null (pre-Sprint-58 row): derived from file name via template registry.
        var service = CreateService(new List<FileThemeRow>
        {
            new(sourceId, "CMP 2022 - 3. RFP Analysis.docx", null, null)
        });

        var result = await service.ResolveSourceIdsAsync(new[] { "Analysis" });

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { sourceId }, result.Value);
    }

    [Fact]
    public async Task ResolveSourceIdsAsync_returns_empty_for_unmatched_theme()
    {
        var sourceId = Guid.NewGuid();
        var service = CreateService(new List<FileThemeRow>
        {
            new(sourceId, "CMP 2026 - 3. RFP Analysis.docx", "3.0 - RFP Analysis", new[] { "Analysis" })
        });

        var result = await service.ResolveSourceIdsAsync(new[] { "As-Built" });

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task ResolveSourceIdsAsync_combines_themes_as_union()
    {
        var analysisId = Guid.NewGuid();
        var asBuiltId = Guid.NewGuid();
        var service = CreateService(new List<FileThemeRow>
        {
            new(analysisId, "CMP 2026 - 3. RFP Analysis.docx", "3.0 - RFP Analysis", new[] { "Analysis" }),
            new(asBuiltId, "CMP 2026 - As Built.docx", "2.0 - As Built", new[] { "As-Built" })
        });

        var result = await service.ResolveSourceIdsAsync(new[] { "Analysis", "As-Built" });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Contains(analysisId, result.Value);
        Assert.Contains(asBuiltId, result.Value);
    }

    [Fact]
    public async Task ResolveSourceIdsAsync_returns_empty_when_no_themes_requested()
    {
        var service = CreateService(new List<FileThemeRow>
        {
            new(Guid.NewGuid(), "CMP 2026 - 3. RFP Analysis.docx", "3.0 - RFP Analysis", new[] { "Analysis" })
        });

        var result = await service.ResolveSourceIdsAsync(Array.Empty<string>());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task GetThemesWithCountsAsync_includes_registry_themes_with_zero_documents()
    {
        var service = CreateService(new List<FileThemeRow>
        {
            new(Guid.NewGuid(), "CMP 2026 - 3. RFP Analysis.docx", "3.0 - RFP Analysis", new[] { "Analysis" })
        });

        var result = await service.GetThemesWithCountsAsync();

        Assert.True(result.IsSuccess);
        var analysis = result.Value!.First(theme => theme.Theme == "Analysis");
        Assert.Equal(1, analysis.DocumentCount);
        // Registry declares Analysis; metadata rows fall under it.
        Assert.Single(result.Value!);
    }

    [Fact]
    public async Task GetThemesWithCountsAsync_counts_uncategorized_rows()
    {
        var service = CreateService(new List<FileThemeRow>
        {
            new(Guid.NewGuid(), "Q3 Financial Report.xlsx", null, null)
        });

        var result = await service.GetThemesWithCountsAsync();

        Assert.True(result.IsSuccess);
        var uncategorized = result.Value!.First(theme => theme.Theme == "Uncategorized");
        Assert.Equal(1, uncategorized.DocumentCount);
    }

    [Fact]
    public async Task ResolveSourceIdsAsync_matches_multi_theme_document_by_any_theme()
    {
        var sourceId = Guid.NewGuid();
        var service = CreateService(new List<FileThemeRow>
        {
            new(sourceId, "CMP 2026 - 3. RFP Analysis.docx", "3.0 - RFP Analysis", new[] { "Analysis", "As-Built" })
        });

        var result = await service.ResolveSourceIdsAsync(new[] { "As-Built" });

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { sourceId }, result.Value);
    }

    [Fact]
    public async Task GetThemesWithCountsAsync_counts_multi_theme_document_in_each_theme()
    {
        var service = CreateService(new List<FileThemeRow>
        {
            new(Guid.NewGuid(), "CMP 2026 - 3. RFP Analysis.docx", "3.0 - RFP Analysis", new[] { "Analysis", "As-Built" })
        });

        var result = await service.GetThemesWithCountsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.First(theme => theme.Theme == "Analysis").DocumentCount);
        Assert.Equal(1, result.Value!.First(theme => theme.Theme == "As-Built").DocumentCount);
    }

    private static KnowledgeThemeService CreateService(IReadOnlyList<FileThemeRow> rows)
    {
        return new KnowledgeThemeService(
            new FakeThemeMetadataRepository(rows),
            new DocumentTemplateRegistry());
    }

    private sealed class FakeThemeMetadataRepository : IMetadataRepository
    {
        private readonly IReadOnlyList<FileThemeRow> _rows;

        public FakeThemeMetadataRepository(IReadOnlyList<FileThemeRow> rows)
        {
            _rows = rows;
        }

        public Task<Result<FileMetadata>> GetAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<FileMetadata>.Failure("not used"));

        public Task<Result<FileMetadata>> SaveAsync(FileMetadata metadata, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<FileMetadata>.Success(metadata));

        public Task<Result<PagedResult<FileMetadata>>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<PagedResult<FileMetadata>>.Success(new PagedResult<FileMetadata>(new List<FileMetadata>(), 1, 10, 0)));

        public Task<Result> DeleteAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());

        public Task<Result<IReadOnlyList<FileThemeRow>>> ListThemeRowsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<FileThemeRow>>.Success(_rows));
    }
}