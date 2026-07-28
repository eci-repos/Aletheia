namespace Aletheia.RAGS.Abstractions.Models;

public class ExplanationRequest
{
    public string Query { get; set; } = string.Empty;

    public int TopK { get; set; } = 5;

    public string? TargetSourceId { get; set; }
}
