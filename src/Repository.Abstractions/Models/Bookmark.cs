using Aletheia.Foundation.Shared;

namespace Aletheia.Repository.Abstractions.Models;

public class Bookmark 
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public string TargetId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Type { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
