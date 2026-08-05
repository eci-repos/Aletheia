using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.Repository.API.Controllers;
using Aletheia.Repository.API.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Repository.UnitTests.Controllers;

public class RagsControllerTests
{
    [Fact]
    public async Task Status_returns_snapshot_when_service_succeeds()
    {
        var snapshot = new RagsStatusSnapshot(
            EmbeddedChunkCount: 42,
            IngestedSourceCount: 3,
            RegisteredDocumentCount: 5,
            TemplateGateSkipCount: 1,
            ExtractionFailureCount: 0,
            TemplateGateSkips: new[] { "Renamed File.pdf" },
            RecentUploadJobs: new List<UploadJobSummary>());
        var statusService = new Mock<IRagsStatusService>();
        statusService
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RagsStatusSnapshot>.Success(snapshot));
        var controller = new RagsController(new Mock<IRagsService>().Object, statusService.Object);

        var result = await controller.Status(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(snapshot, ok.Value);
    }

    [Fact]
    public async Task Status_returns_bad_request_when_service_fails()
    {
        var statusService = new Mock<IRagsStatusService>();
        statusService
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RagsStatusSnapshot>.Failure("db unavailable"));
        var controller = new RagsController(new Mock<IRagsService>().Object, statusService.Object);

        var result = await controller.Status(CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("db unavailable", badRequest.Value!.ToString());
    }
}
