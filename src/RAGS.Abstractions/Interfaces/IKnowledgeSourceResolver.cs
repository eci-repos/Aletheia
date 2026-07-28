using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IKnowledgeSourceResolver
{
    Task<Result<KnowledgeSource?>> ResolveAsync(string userMessage, CancellationToken cancellationToken = default);
}
