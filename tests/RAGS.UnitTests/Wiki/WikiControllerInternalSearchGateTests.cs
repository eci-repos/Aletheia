using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.API.Controllers;
using Aletheia.Repository.API.Services;
using Microsoft.AspNetCore.Mvc;
using RAGS.UnitTests.TestSupport;

namespace RAGS.UnitTests.Wiki;

public sealed class WikiControllerInternalSearchGateTests
{
    [Fact]
    public async Task SearchAsync_with_internal_mode_returns_not_found_when_hidden()
    {
        var controller = new WikiController(
            new FakeWikiService(),
            new FakeIngestionJobs(),
            new FakeInternalSearchGate(showInternalSearch: false));

        var result = await controller.SearchAsync("query", mode: "graphrag");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task SearchAsync_with_wrags_mode_is_allowed_when_hidden()
    {
        var service = new FakeWikiService();
        var controller = new WikiController(
            service,
            new FakeIngestionJobs(),
            new FakeInternalSearchGate(showInternalSearch: false));

        var result = await controller.SearchAsync("Project Helios", mode: "wrags");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal(1, service.SearchCalls);
    }

    [Fact]
    public async Task SearchAsync_with_internal_mode_is_allowed_when_enabled()
    {
        var service = new FakeWikiService();
        var controller = new WikiController(
            service,
            new FakeIngestionJobs(),
            new FakeInternalSearchGate(showInternalSearch: true));

        var result = await controller.SearchAsync("query", mode: "graphrag");

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, service.SearchCalls);
    }

    [Fact]
    public async Task RegenerateJob_returns_not_found_when_hidden()
    {
        var controller = new WikiController(
            new FakeWikiService(),
            new FakeIngestionJobs(),
            new FakeInternalSearchGate(showInternalSearch: false));

        var result = controller.RegenerateJob(new WikiSearchRequest { Query = "RFP", Mode = "wrags" });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void RegenerateBriefs_returns_accepted_job_snapshot()
    {
        var jobs = new FakeIngestionJobs();
        var controller = new WikiController(
            new FakeWikiService(),
            jobs,
            new FakeInternalSearchGate(showInternalSearch: false));

        var result = controller.RegenerateBriefs();

        var accepted = Assert.IsType<AcceptedResult>(result);
        var job = Assert.IsType<IngestionJobSnapshot>(accepted.Value);
        Assert.Equal("DocumentBriefs", job.Kind);
        Assert.Equal(1, jobs.DocumentBriefsCalls);
    }

    private sealed class FakeWikiService : IWragsWikiService
    {
        public int SearchCalls { get; private set; }

        public Task<Result<IReadOnlyList<WikiPage>>> SearchAsync(WikiSearchRequest request, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            IReadOnlyList<WikiPage> pages = new[]
            {
                new WikiPage(Guid.NewGuid(), request.Query, "Stored page", "Summary.", new[] { Guid.NewGuid() }, new[] { "citation" }, "document-brief", primarySourceId: Guid.NewGuid())
            };
            return Task.FromResult(Result<IReadOnlyList<WikiPage>>.Success(pages));
        }

        public Task<Result<IReadOnlyList<WikiPage>>> RegenerateAsync(WikiSearchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<WikiPage>>.Success(Array.Empty<WikiPage>()));
        }

        public Task<Result<IReadOnlyList<WikiPage>>> GetRecentAsync(int take, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<WikiPage>>.Success(Array.Empty<WikiPage>()));
        }

        public Task<Result<WikiPage?>> GetAsync(Guid pageId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<WikiPage?>.Success(null));
        }

        public Task<Result<IReadOnlyList<WikiPageLink>>> GetRelatedAsync(Guid pageId, int take, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<WikiPageLink>>.Success(Array.Empty<WikiPageLink>()));
        }

        public Task<Result<IReadOnlyList<WikiPageHistoryEntry>>> GetHistoryAsync(Guid pageId, int take, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<WikiPageHistoryEntry>>.Success(Array.Empty<WikiPageHistoryEntry>()));
        }

        public Task<Result<WikiPage?>> UpdateStatusAsync(Guid pageId, WikiPageStatusUpdate update, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<WikiPage?>.Success(null));
        }

        public Task<Result<WikiPage?>> UpdatePageAsync(Guid pageId, WikiPageEditRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<WikiPage?>.Success(null));
        }

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(WikiSearchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
        }
    }

    private sealed class FakeIngestionJobs : IIngestionJobService
    {
        public int DocumentBriefsCalls { get; private set; }

        public IngestionJobSnapshot EnqueueUploadedFile(Guid sourceId, string sourceName, string contentType, string tempFilePath, long sizeBytes)
        {
            return CreateJob(sourceId, "UploadIngestion");
        }

        public IngestionJobSnapshot EnqueueContent(IngestionJobEngine engine, Guid sourceId, string content, string? sourceName = null)
        {
            return CreateJob(sourceId, $"{engine}Ingestion");
        }

        public IngestionJobSnapshot EnqueueWikiRegeneration(WikiSearchRequest request)
        {
            return CreateJob(Guid.NewGuid(), "WikiRegeneration");
        }

        public IngestionJobSnapshot EnqueueRagsRepair(string? query = null)
        {
            return CreateJob(Guid.Empty, "RagsRepair");
        }

        public IngestionJobSnapshot EnqueueDocumentBriefs(Guid? sourceId = null, string? sourceName = null)
        {
            DocumentBriefsCalls++;
            return CreateJob(sourceId ?? Guid.Empty, "DocumentBriefs");
        }

        public IngestionJobSnapshot EnqueueReembed()
        {
            return CreateJob(Guid.Empty, "ReembedIngestion");
        }

        public IReadOnlyList<IngestionJobSnapshot> List(int take = 50)
        {
            return Array.Empty<IngestionJobSnapshot>();
        }

        public IngestionJobSnapshot? Get(Guid jobId)
        {
            return null;
        }

        public bool HasActiveIngestion(Guid sourceId)
        {
            return false;
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
