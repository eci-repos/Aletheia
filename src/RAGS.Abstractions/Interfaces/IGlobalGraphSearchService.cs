using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IGlobalGraphSearchService
{
    /// <summary>
    /// Executes a global (organization-wide) search using map-reduce over community summaries.
    /// </summary>
    Task<Result<GlobalSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);
}
