namespace Aletheia.RAGS.Abstractions.Models;

public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public ChatCompletionStats? Stats { get; set; }

    /// <summary>Maps the bracketed citation numbers in <see cref="Content"/> (e.g. [1]) to the source chunk they refer to, so the UI can link them to the document viewer.</summary>
    public IReadOnlyList<ChatCitation> Citations { get; set; } = Array.Empty<ChatCitation>();
}

/// <summary>A single bracketed citation in a Copilot answer, resolved to its source chunk for the document viewer.</summary>
public sealed record ChatCitation(
    int Number,
    Guid SourceId,
    int? PageNumber,
    string LeadingPhrase);

public sealed class ChatCompletionStats
{
    public double ElapsedSeconds { get; set; }

    public int EstimatedPromptTokens { get; set; }

    public int EstimatedCompletionTokens { get; set; }

    public double TokensPerSecond { get; set; }

    public int RetrievedContextCount { get; set; }

    public int CitationCount { get; set; }

    public double MaxRetrievalScore { get; set; }

    public double AverageRetrievalScore { get; set; }

    public double AlignmentConfidence { get; set; }

    public string ConfidenceBasis { get; set; } = string.Empty;
}
