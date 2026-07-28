using Aletheia.Foundation.Shared;

namespace Aletheia.Repository.Abstractions.Models;

public class RetentionPolicy 
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string ResourceType { get; set; } = string.Empty;

    public TimeSpan Duration { get; set; }

    public string ActionOnExpiry { get; set; } = "archive";
}
