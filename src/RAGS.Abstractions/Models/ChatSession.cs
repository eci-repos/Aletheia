namespace Aletheia.RAGS.Abstractions.Models;

public class ChatSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = "New Chat";

    public List<ChatMessage> Messages { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Knowledge themes (Sprint 58) scoping this session. Empty = all documents.</summary>
    public List<string> ThemeFilter { get; set; } = new();
}