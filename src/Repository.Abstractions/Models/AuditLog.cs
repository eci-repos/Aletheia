using Aletheia.Foundation.Shared;

namespace Aletheia.Repository.Abstractions.Models;

public class AuditLog 
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Action { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string ResourceId { get; set; } = string.Empty;

    public string? Details { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
