using Aletheia.Foundation.Shared;

namespace Aletheia.Repository.Abstractions.Models;

public class Comment 
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TargetId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
