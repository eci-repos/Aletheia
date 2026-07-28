namespace Aletheia.RAGS.Abstractions.Models;

public sealed class PromptAnalysis
{
    public string NormalizedPrompt { get; init; } = string.Empty;

    public ChatExecutionMode SuggestedMode { get; init; } = ChatExecutionMode.FastPath;

    public IReadOnlyList<string> DetectedIntentSignals { get; init; } = Array.Empty<string>();

    public bool IsBroadCorpusRequest { get; init; }

    public bool IsExpensive { get; init; }

    public bool RequiresApproval { get; init; }

    public int EstimatedPromptTokens { get; init; }

    public int Confidence { get; init; }
}
