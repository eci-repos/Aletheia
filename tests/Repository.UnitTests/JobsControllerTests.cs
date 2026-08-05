using Aletheia.Repository.API.Controllers;
using Aletheia.Repository.API.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Repository.UnitTests.Controllers;

public class JobsControllerTests
{
    [Fact]
    public void ReembedRags_returns_accepted_job()
    {
        var jobId = Guid.NewGuid();
        var job = new IngestionJobSnapshot(
            jobId,
            "ReembedIngestion",
            "Re-embed all registered documents",
            "Queued",
            "Queued",
            0,
            0,
            1,
            string.Empty,
            Guid.Empty,
            null,
            DateTimeOffset.UtcNow,
            null,
            DateTimeOffset.UtcNow,
            null,
            null);
        var jobs = new Mock<IIngestionJobService>();
        jobs.Setup(x => x.EnqueueReembed()).Returns(job);
        var controller = new JobsController(jobs.Object);

        var result = controller.ReembedRags();

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Same(job, accepted.Value);
        jobs.Verify(x => x.EnqueueReembed(), Times.Once);
    }
}
