using Aletheia.Foundation.Shared;

namespace Aletheia.Repository.Abstractions.Models;

public class SharedWorkspace 
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string OwnerId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<WorkspaceMember> Members { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class WorkspaceMember
{
    public string UserId { get; set; } = string.Empty;

    public string Role { get; set; } = "viewer";

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}
