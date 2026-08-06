using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
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
}