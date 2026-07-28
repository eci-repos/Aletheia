using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Aletheia.RAGS.Application.SemanticKernel;

public sealed class SemanticKernelChatService : IChatService
{
    private readonly Kernel _kernel;

    public SemanticKernelChatService(Kernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    }

    public async Task<Result<ChatMessage>> ChatAsync(ChatSession session, string userMessage, CancellationToken cancellationToken = default)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        if (string.IsNullOrWhiteSpace(userMessage))
        {
            throw new ArgumentException("User message is required.", nameof(userMessage));
        }

        try
        {
            var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();

            foreach (var msg in session.Messages)
            {
                if (string.Equals(msg.Role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    history.AddUserMessage(msg.Content);
                }
                else
                {
                    history.AddAssistantMessage(msg.Content);
                }
            }

            history.AddUserMessage(userMessage);

            var response = await chatCompletion.GetChatMessageContentAsync(
                history,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var content = response.Content ?? string.Empty;
            var assistantMsg = new ChatMessage { Role = "assistant", Content = content };

            return Result<ChatMessage>.Success(assistantMsg);
        }
        catch (Exception ex)
        {
            return Result<ChatMessage>.Failure($"Chat failed: {ex.Message}");
        }
    }
}
