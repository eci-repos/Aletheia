using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IKnowledgeSourceIngestionService
{
    Task<Result<bool>> EnsureIngestedAsync(
        KnowledgeSource source,
        CancellationToken cancellationToken = default);
}
