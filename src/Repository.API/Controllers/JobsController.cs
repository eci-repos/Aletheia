using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aletheia.Repository.API.Controllers;

[ApiController]
[Route("api/jobs")]
[Authorize]
public sealed class JobsController : ControllerBase
{
    private readonly IIngestionJobService _jobs;

    public JobsController(IIngestionJobService jobs)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<IngestionJobSnapshot>), StatusCodes.Status200OK)]
    public IActionResult List([FromQuery] int take = 50)
    {
        return Ok(_jobs.List(take));
    }

    [HttpGet("{jobId:guid}")]
    [ProducesResponseType(typeof(IngestionJobSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Get(Guid jobId)
    {
        var job = _jobs.Get(jobId);
        return job is null ? NotFound(new { error = "Job not found." }) : Ok(job);
    }

    [HttpPost("rags/ingest")]
    [ProducesResponseType(typeof(IngestionJobSnapshot), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult IngestRags([FromBody] IngestionRequest request)
    {
        return Accepted(_jobs.EnqueueContent(IngestionJobEngine.Rags, request.SourceId, request.Content, request.SourceName));
    }

    [HttpPost("graphrag/ingest")]
    [ProducesResponseType(typeof(IngestionJobSnapshot), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult IngestGraphRag([FromBody] IngestionRequest request)
    {
        return Accepted(_jobs.EnqueueContent(IngestionJobEngine.GraphRag, request.SourceId, request.Content, request.SourceName));
    }

    [HttpPost("lazygraphrag/ingest")]
    [ProducesResponseType(typeof(IngestionJobSnapshot), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult IngestLazyGraphRag([FromBody] IngestionRequest request)
    {
        return Accepted(_jobs.EnqueueContent(IngestionJobEngine.LazyGraphRag, request.SourceId, request.Content, request.SourceName));
    }

    [HttpPost("rags/repair")]
    [ProducesResponseType(typeof(IngestionJobSnapshot), StatusCodes.Status202Accepted)]
    public IActionResult RepairRags([FromQuery] string? query = null)
    {
        return Accepted(_jobs.EnqueueRagsRepair(query));
    }
}
