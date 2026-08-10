using System.Security.Claims;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Repository.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CopilotController : ControllerBase
{
    private readonly ICopilotService _copilotService;
    private readonly IChatPlanApprovalService _planApprovalService;
    private readonly IChatExecutionService _chatExecutionService;

    public CopilotController(
        ICopilotService copilotService,
        IChatPlanApprovalService planApprovalService,
        IChatExecutionService chatExecutionService)
    {
        _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));
        _planApprovalService = planApprovalService ?? throw new ArgumentNullException(nameof(planApprovalService));
        _chatExecutionService = chatExecutionService ?? throw new ArgumentNullException(nameof(chatExecutionService));
    }

    [HttpPost("chat")]
    public async Task<ActionResult<ChatMessage>> Chat([FromBody] ChatPayload payload, CancellationToken cancellationToken)
    {
        if (payload is null)
        {
            return BadRequest(new { error = "Payload is required." });
        }

        var result = await _copilotService
            .ChatAsync(
                payload.Session,
                payload.Message,
                new ChatRequestOptions { OutputFormat = payload.OutputFormat, ThemeFilter = payload.ThemeFilter ?? payload.Session.ThemeFilter },
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("summarize")]
    public async Task<ActionResult<SummaryResponse>> Summarize([FromBody] SummaryRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request is required." });
        }

        var result = await _copilotService.SummarizeAsync(request, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("explain")]
    public async Task<ActionResult<ExplanationResponse>> Explain([FromBody] ExplanationRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request is required." });
        }

        var result = await _copilotService.ExplainAsync(request, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("discover")]
    public async Task<ActionResult<DiscoveryResponse>> Discover([FromBody] DiscoveryRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request is required." });
        }

        var result = await _copilotService.DiscoverAsync(request, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("plan")]
    public async Task<ActionResult<ChatPlanRecord>> Plan([FromBody] PlanPayload payload, CancellationToken cancellationToken)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.Prompt))
        {
            return BadRequest(new { error = "Prompt is required." });
        }

        var result = await _planApprovalService
            .CreatePlanAsync(payload.Prompt, payload.SessionId, payload.HistoryMessages, payload.ThemeFilter, CurrentUserId, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(new { error = result.Error });
    }

    [HttpPost("plans/{planId:guid}/approve")]
    public async Task<ActionResult<ChatPlanRecord>> ApprovePlan(Guid planId, CancellationToken cancellationToken)
    {
        var result = await _planApprovalService
            .ApproveAsync(planId, User.Identity?.Name, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpPost("plans/{planId:guid}/cancel")]
    public async Task<ActionResult<ChatPlanRecord>> CancelPlan(Guid planId, [FromBody] CancelPayload? payload, CancellationToken cancellationToken)
    {
        var result = await _planApprovalService
            .CancelAsync(planId, payload?.Reason, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpGet("plans/{planId:guid}")]
    public async Task<ActionResult<ChatPlanRecord>> GetPlan(Guid planId, CancellationToken cancellationToken)
    {
        var result = await _planApprovalService
            .GetAsync(planId, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        if (result.Value is null)
        {
            return NotFound(new { error = "Plan not found." });
        }

        return Ok(result.Value);
    }

    [HttpPost("plans/{planId:guid}/execute")]
    public async Task<ActionResult<ChatJobSnapshot>> ExecutePlan(Guid planId, CancellationToken cancellationToken)
    {
        var result = await _chatExecutionService
            .StartAsync(planId, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Accepted(result.Value);
    }

    [HttpGet("jobs/chat/{jobId:guid}")]
    public async Task<ActionResult<ChatJobSnapshot>> GetChatJob(Guid jobId, CancellationToken cancellationToken)
    {
        var result = await _chatExecutionService
            .GetStatusAsync(jobId, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        if (result.Value is null)
        {
            return NotFound(new { error = "Job not found." });
        }

        return Ok(result.Value);
    }

    [HttpPost("jobs/chat/{jobId:guid}/cancel")]
    public async Task<IActionResult> CancelChatJob(Guid jobId, CancellationToken cancellationToken)
    {
        var result = await _chatExecutionService
            .CancelAsync(jobId, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess ? NoContent() : BadRequest(new { error = result.Error });
    }

    [HttpGet("jobs/chat")]
    public ActionResult<IReadOnlyList<ChatJobSnapshot>> ListChatJobs([FromQuery] int take = 50)
    {
        return Ok(_chatExecutionService.List(take));
    }

    [HttpGet("plans/{planId:guid}/progress")]
    public async Task<ActionResult<ChatProgressRecord>> GetPlanProgress(Guid planId, CancellationToken cancellationToken)
    {
        var planResult = await _planApprovalService.GetAsync(planId, cancellationToken).ConfigureAwait(false);
        if (planResult.IsFailure)
        {
            return BadRequest(new { error = planResult.Error });
        }

        if (planResult.Value is null)
        {
            return NotFound(new { error = "Plan not found." });
        }

        var jobs = _chatExecutionService.List(200).Where(job => job.PlanId == planId).ToList();
        if (jobs.Count == 0)
        {
            // The plan exists but has not been executed yet. This is a normal "waiting for the user
            // to approve" state, not an error — return it as 200 with an empty JobId so clients can
            // tell "plan not started" apart from "plan not found" (404 above). Sprint 59 fix: without
            // this, a Web client that restored a pending plan from browser state polled this endpoint
            // and spun forever on 404.
            return Ok(new ChatProgressRecord
            {
                JobId = Guid.Empty,
                PlanId = planResult.Value!.PlanId,
                Prompt = planResult.Value.Prompt,
                Status = ChatJobStatus.Queued,
                CreatedAt = planResult.Value.CreatedAt
            });
        }

        var jobId = jobs.OrderByDescending(job => job.CreatedAt).First().JobId;
        var progress = await _chatExecutionService
            .GetProgressAsync(jobId, cancellationToken)
            .ConfigureAwait(false);
        if (progress.IsFailure)
        {
            return BadRequest(new { error = progress.Error });
        }

        if (progress.Value is null)
        {
            return NotFound(new { error = "Progress not found." });
        }

        return Ok(progress.Value);
    }

    [HttpGet("jobs/chat/{jobId:guid}/telemetry")]
    public async Task<ActionResult<ChatExecutionTelemetry>> GetTelemetry(Guid jobId, CancellationToken cancellationToken)
    {
        var progress = await _chatExecutionService
            .GetProgressAsync(jobId, cancellationToken)
            .ConfigureAwait(false);

        if (progress.IsFailure)
        {
            return BadRequest(new { error = progress.Error });
        }

        if (progress.Value is null)
        {
            return NotFound(new { error = "Progress not found." });
        }

        if (progress.Value.Telemetry is null)
        {
            return NotFound(new { error = "Telemetry not yet available." });
        }

        return Ok(progress.Value.Telemetry);
    }

    private string? CurrentUserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public class ChatPayload
    {
        public ChatSession Session { get; set; } = new();
        public string Message { get; set; } = string.Empty;
        public string? OutputFormat { get; set; }

        /// <summary>Knowledge themes (Sprint 58). Null/empty falls back to the session's ThemeFilter, then to all documents.</summary>
        public IReadOnlyList<string>? ThemeFilter { get; set; }
    }

    public class PlanPayload
    {
        public string Prompt { get; set; } = string.Empty;

        public Guid? SessionId { get; set; }

        public IReadOnlyList<ChatMessage>? HistoryMessages { get; set; }

        public IReadOnlyList<string>? ThemeFilter { get; set; }
    }

    public class CancelPayload
    {
        public string? Reason { get; set; }
    }
}
