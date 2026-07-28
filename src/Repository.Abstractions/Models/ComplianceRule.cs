using Aletheia.Foundation.Shared;

namespace Aletheia.Repository.Abstractions.Models;

public class ComplianceRule 
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string RuleType { get; set; } = string.Empty;

    public string Condition { get; set; } = string.Empty;

    public string Severity { get; set; } = "warning";
}
