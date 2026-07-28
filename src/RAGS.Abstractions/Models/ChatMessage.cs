namespace Aletheia.RAGS.Abstractions.Models;

public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public ChatCompletionStats? Stats { get; set; }
}

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
