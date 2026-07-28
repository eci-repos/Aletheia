using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IChatPlanningService
{
    Task<Result<PromptAnalysis>> AnalyzePromptAsync(string prompt, CancellationToken cancellationToken = default);

    Task<Result<ChatExecutionPlan>> CreatePlanAsync(string prompt, PromptAnalysis? analysis = null, CancellationToken cancellationToken = default);

    Task<Result<ChatExecutionPlan>> EstimatePlanAsync(ChatExecutionPlan plan, CancellationToken cancellationToken = default);

    bool RequiresApproval(ChatExecutionPlan plan);
}
