using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>
/// Reports whether knowledge-graph summaries exist and how much of the graph is summarized,
/// globally and per source. Ground truth is the graph itself: community nodes with a stored
/// <c>summary</c> property are "summarized"; entity nodes carry the <c>sourceId</c> they belong to.
/// </summary>
public interface ISummariesStatusService
{
    Task<Result<SummariesStatusSnapshot>> GetAsync(CancellationToken cancellationToken = default);
}
