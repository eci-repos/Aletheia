using Aletheia.Foundation.Shared;

namespace Aletheia.Repository.Abstractions.Models;

public class PiiDetectionResult
{
    public bool PiiDetected { get; set; }

    public List<PiiMatch> Matches { get; set; } = new();
}

public class PiiMatch
{
    public string PiiType { get; set; } = string.Empty;

    public string MaskedValue { get; set; } = string.Empty;

    public int StartIndex { get; set; }

    public int EndIndex { get; set; }
}
