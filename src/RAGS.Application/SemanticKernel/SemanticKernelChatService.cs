using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Aletheia.RAGS.Application.SemanticKernel;

public sealed class SemanticKernelChatService : IChatService
{
    private readonly Kernel _kernel;
    private readonly ChatAgentOptions _options;
    private readonly IChatAgentInstructionProvider? _instructionProvider;
    private readonly IAgentInstructionResolver? _instructionResolver;

    public SemanticKernelChatService(
        Kernel kernel,
        IOptions<ChatAgentOptions>? options = null,
        IChatAgentInstructionProvider? instructionProvider = null,
        IAgentInstructionResolver? instructionResolver = null)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _options = options?.Value ?? new ChatAgentOptions();
        _instructionProvider = instructionProvider;
        _instructionResolver = instructionResolver;
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
            history.AddSystemMessage(await BuildSystemPromptAsync(cancellationToken).ConfigureAwait(false));

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

    private async Task<string> BuildSystemPromptAsync(CancellationToken cancellationToken)
    {
        var prompt = await ResolveAssistantPersonaAsync(cancellationToken).ConfigureAwait(false);

        if (_options.BehaviorFlags.CiteSources)
        {
            prompt += "\nCite supporting evidence with bracketed citation numbers such as [1], and reference the source artifact or wiki page when possible.";
        }

        if (_options.BehaviorFlags.RequireRepositoryLookupBeforeAnswer)
        {
            prompt += $"\nBefore answering any substantive question, invoke the repository search tool ({_options.ToolNames.SearchRepository}) to retrieve relevant documents.";
        }

        var externalInstructions = _instructionProvider is not null
            ? await _instructionProvider.GetInstructionsAsync(cancellationToken).ConfigureAwait(false)
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(externalInstructions))
        {
            prompt += $"\n\nRepository orchestration playbook:\n{externalInstructions}";
        }

        return prompt;
    }

    /// <summary>Sprint 77: the assistant persona resolves through <see cref="IAgentInstructionResolver"/>
    /// (app_settings override → config baseline); the composed <c>ChatAgentOptions</c> persona is the
    /// backward-compat fallback when no resolver is present.</summary>
    private async Task<string> ResolveAssistantPersonaAsync(CancellationToken cancellationToken)
    {
        if (_instructionResolver is not null)
        {
            var resolved = await _instructionResolver
                .ResolveAsync(AgentInstructionRoles.CopilotAssistant, cancellationToken)
                .ConfigureAwait(false);
            if (resolved.IsSuccess && !string.IsNullOrWhiteSpace(resolved.Value!.Value))
            {
                return resolved.Value.Value;
            }
        }

        return ComposeAssistantPersona();
    }

    private string ComposeAssistantPersona()
    {
        return $"""
            {_options.Role}
            {_options.RepositoryDescription}
            {_options.Mandate}
            When the user's question concerns repository content such as projects, RFPs, contracts, requirements, wiki pages, teams, schedules, rules, or mandates, you must ground your answer exclusively in retrieved repository documents.
            You have no access to general internet knowledge, LLM training data, market data, or external facts.
            If the requested information is not present in the retrieved context, respond with: {_options.NoInformationResponse}
            """;
    }
}
