using Aletheia.Repository.API.Controllers;
using Aletheia.Repository.API.Services;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.AspNetCore.Mvc;

namespace RAGS.UnitTests.BackgroundJobs;

public sealed class JobsControllerTests
{
    [Fact]
    public void List_returns_jobs_from_service()
    {
        var service = new FakeIngestionJobService();
        var controller = new JobsController(service);

        var result = controller.List();

        var ok = Assert.IsType<OkObjectResult>(result);
        var jobs = Assert.IsAssignableFrom<IReadOnlyList<IngestionJobSnapshot>>(ok.Value);
        Assert.Single(jobs);
    }

    [Fact]
    public void Get_returns_not_found_for_missing_job()
    {
        var controller = new JobsController(new FakeIngestionJobService());

        var result = controller.Get(Guid.NewGuid());

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void IngestGraphRag_returns_accepted_job_snapshot()
    {
        var controller = new JobsController(new FakeIngestionJobService());
        var request = new Aletheia.RAGS.Abstractions.Models.IngestionRequest(Guid.NewGuid(), "content");

        var result = controller.IngestGraphRag(request);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var job = Assert.IsType<IngestionJobSnapshot>(accepted.Value);
        Assert.Equal("GraphRagIngestion", job.Kind);
        Assert.Equal("Queued", job.Status);
    }

    [Fact]
    public void RepairRags_returns_accepted_job_snapshot()
    {
        var controller = new JobsController(new FakeIngestionJobService());

        var result = controller.RepairRags("RFP");

        var accepted = Assert.IsType<AcceptedResult>(result);
        var job = Assert.IsType<IngestionJobSnapshot>(accepted.Value);
        Assert.Equal("RagsRepair", job.Kind);
        Assert.Equal("Queued", job.Status);
        Assert.Equal("RFP", job.SourceName);
    }

    private sealed class FakeIngestionJobService : IIngestionJobService
    {
        private readonly IngestionJobSnapshot _job = CreateJob(Guid.NewGuid(), "RagsIngestion");

        public IngestionJobSnapshot EnqueueUploadedFile(
            Guid sourceId,
            string sourceName,
            string contentType,
            string tempFilePath,
            long sizeBytes)
        {
            return CreateJob(sourceId, "UploadIngestion");
        }

        public IngestionJobSnapshot EnqueueContent(
            IngestionJobEngine engine,
            Guid sourceId,
            string content,
            string? sourceName = null)
        {
            return CreateJob(sourceId, $"{engine}Ingestion");
        }

        public IngestionJobSnapshot EnqueueWikiRegeneration(WikiSearchRequest request)
        {
            return CreateJob(Guid.NewGuid(), "WikiRegeneration");
        }

        public IngestionJobSnapshot EnqueueDocumentBriefs(Guid? sourceId = null, string? sourceName = null)
        {
            return CreateJob(sourceId ?? Guid.Empty, "DocumentBriefs");
        }

        public IngestionJobSnapshot EnqueueReembed()
        {
            return CreateJob(Guid.Empty, "ReembedIngestion");
        }

        public IngestionJobSnapshot EnqueueRagsRepair(string? query = null)
        {
            var job = CreateJob(Guid.Empty, "RagsRepair");
            return job with { SourceName = query };
        }

        public IReadOnlyList<IngestionJobSnapshot> List(int take = 50)
        {
            return new[] { _job };
        }

        public IngestionJobSnapshot? Get(Guid jobId)
        {
            return jobId == _job.JobId ? _job : null;
        }

        private static IngestionJobSnapshot CreateJob(Guid sourceId, string kind)
        {
            var now = DateTimeOffset.UtcNow;
            return new IngestionJobSnapshot(
                Guid.NewGuid(),
                kind,
                "test job",
                "Queued",
                "Queued",
                0,
                0,
                100,
                "Waiting",
                sourceId,
                "source.txt",
                now,
                null,
                now,
                null,
                null);
        }
    }
}
