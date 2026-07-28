using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IChatService
{
    Task<Result<ChatMessage>> ChatAsync(ChatSession session, string userMessage, CancellationToken cancellationToken = default);
}
