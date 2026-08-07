using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.API.Services;
using Repository.API.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Repository.UnitTests.Controllers;

public class KnowledgeControllerTests
{
    [Fact]
    public async Task GetThemes_returns_theme_counts_when_service_succeeds()
    {
        var themes = new List<KnowledgeThemeCount>
        {
            new("Analysis", 2),
            new("As-Built", 0)
        };
        var themeService = new Mock<IKnowledgeThemeService>();
        themeService
            .Setup(x => x.GetThemesWithCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<KnowledgeThemeCount>>.Success(themes));
        var controller = new KnowledgeController(themeService.Object);

        var result = await controller.GetThemes(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(themes, ok.Value);
    }

    [Fact]
    public async Task GetThemes_returns_server_error_when_service_fails()
    {
        var themeService = new Mock<IKnowledgeThemeService>();
        themeService
            .Setup(x => x.GetThemesWithCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<KnowledgeThemeCount>>.Failure("db unavailable"));
        var controller = new KnowledgeController(themeService.Object);

        var result = await controller.GetThemes(CancellationToken.None);

        var serverError = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, serverError.StatusCode);
    }

    [Fact]
    public async Task GetUncategorized_returns_non_canonical_rows()
    {
        var rows = new List<FileThemeRow>
        {
            new(Guid.NewGuid(), "Q3 Financial Report.xlsx", null, null, "Uncategorized")
        };
        var metadataRepository = new Mock<IMetadataRepository>();
        metadataRepository
            .Setup(x => x.ListUncategorizedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<FileThemeRow>>.Success(rows));
        var controller = new KnowledgeController(
            new Mock<IKnowledgeThemeService>().Object,
            metadataRepository.Object);

        var result = await controller.GetUncategorized(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(rows, ok.Value);
    }

    [Fact]
    public async Task Reevaluate_returns_summary_when_service_succeeds()
    {
        var metadataRepository = new Mock<IMetadataRepository>();
        metadataRepository
            .Setup(x => x.ListUncategorizedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<FileThemeRow>>.Success(new List<FileThemeRow>
            {
                new(Guid.NewGuid(), "Q3 Financial Report.xlsx", null, null, "Uncategorized")
            }));
        metadataRepository
            .Setup(x => x.SetTemplateAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var reevaluationService = new TemplateReevaluationService(metadataRepository.Object);
        var controller = new KnowledgeController(
            new Mock<IKnowledgeThemeService>().Object,
            reevaluationService: reevaluationService);

        var result = await controller.Reevaluate(new TemplateReevaluationRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<TemplateReevaluationSummary>(ok.Value);
        Assert.Equal(1, summary.Evaluated);
        Assert.Equal(1, summary.Uncategorized);
    }
}