using Aletheia.Foundation.Shared;

namespace Aletheia.Repository.Abstractions.Models;

public class Annotation 
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TargetId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string HighlightText { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public int StartOffset { get; set; }

    public int EndOffset { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
