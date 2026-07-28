using Aletheia.Foundation.Shared;

namespace Aletheia.Repository.Abstractions.Models;

public class Permission 
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string ResourceType { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;
}
