using Aletheia.Foundation.Security;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.API.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace RAGS.UnitTests.GraphRAG;

public sealed class SummariesControllerTests
{
    private static SearchResult Result(string content) =>
        new(new Chunk(Guid.NewGuid(), Guid.NewGuid(), content, 0), 0.9f);

    private static (SummariesController Controller, Mock<ISummariesRetrievalService> Retrieval, Mock<ISummariesStatusService> Status) Build(
        IKnowledgeThemeService? themeService = null)
    {
        var retrieval = new Mock<ISummariesRetrievalService>();
        var status = new Mock<ISummariesStatusService>();
        var controller = new SummariesController(retrieval.Object, status.Object, themeService);
        return (controller, retrieval, status);
    }

    [Fact]
    public async Task Retrieve_returns_ok_with_results_when_service_succeeds()
    {
        var (controller, retrieval, _) = Build();
        var results = new List<SearchResult> { Result("summary") };
        retrieval
            .Setup(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Success(results));

        var result = await controller.Retrieve("query");

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
        Assert.Same(results, okResult.Value);
    }

    [Fact]
    public async Task Retrieve_returns_bad_request_when_service_fails()
    {
        var (controller, retrieval, _) = Build();
        retrieval
            .Setup(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Failure("retrieve failed"));

        var result = await controller.Retrieve("query");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Retrieve_resolves_themes_to_source_ids_and_passes_them_to_service()
    {
        var sourceA = Guid.NewGuid();
        var themeService = new Mock<IKnowledgeThemeService>();
        themeService
            .Setup(s => s.ResolveSourceIdsAsync(new[] { "Theme A" }, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Guid>>.Success(new[] { sourceA }));
        var (controller, retrieval, _) = Build(themeService.Object);
        retrieval
            .Setup(s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyList<Guid>?>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchResult>>.Success(new List<SearchResult>()));

        var result = await controller.Retrieve("query", themes: "Theme A");

        Assert.IsType<OkObjectResult>(result);
        retrieval.Verify(
            s => s.RetrieveAsync("query", 5, 10, It.IsAny<CancellationToken>(), It.Is<IReadOnlyList<Guid>?>(ids => ids != null && ids.Contains(sourceA))),
            Times.Once);
    }

    [Fact]
    public async Task Status_returns_ok_with_snapshot_when_service_succeeds()
    {
        var (controller, _, status) = Build();
        var snapshot = new SummariesStatusSnapshot { GraphExists = true, CommunityCount = 3 };
        status
            .Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SummariesStatusSnapshot>.Success(snapshot));

        var result = await controller.Status();

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Same(snapshot, okResult.Value);
    }

    [Fact]
    public void Status_action_is_administrator_only()
    {
        var method = typeof(SummariesController).GetMethod(nameof(SummariesController.Status));
        Assert.NotNull(method);

        var authorize = method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(authorize);
        Assert.Contains(RoleDefinitions.Administrator, authorize!.Roles ?? string.Empty);
    }

    [Fact]
    public void Retrieve_action_is_not_administrator_only()
    {
        // The user-facing Summaries search must NOT be gated behind the internal-search flag or an
        // admin role — it is a first-class user search mode alongside Semantic.
        var method = typeof(SummariesController).GetMethod(nameof(SummariesController.Retrieve));
        Assert.NotNull(method);

        var authorize = method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .ToList();

        Assert.DoesNotContain(authorize, a => !string.IsNullOrEmpty(a.Roles));
    }
}
