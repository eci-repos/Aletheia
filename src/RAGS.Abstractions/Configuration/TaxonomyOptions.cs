namespace Aletheia.RAGS.Abstractions.Configuration;

public sealed class TaxonomyOptions
{
    public const string SectionName = "Taxonomy";

    public List<string> StopWords { get; set; } = new List<string>();
}
