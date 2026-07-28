using Aletheia.Foundation.Shared;

namespace Aletheia.Repository.Abstractions.Models;

public class Role 
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<string> Permissions { get; set; } = new();
}
