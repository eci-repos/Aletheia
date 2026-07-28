namespace Aletheia.RAGS.Abstractions.Models;

public class DiscoveryRequest
{
    public string Topic { get; set; } = string.Empty;

    public int TopK { get; set; } = 10;
}
