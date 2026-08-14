namespace Aletheia.RAGS.Abstractions.Models;

/// <summary>
/// A canonical concept in the normalized lexicon: a controlled-vocabulary entry that maps many
/// surface forms (aliases) to one canonical key. Applied at ingestion (grounded fact extraction
/// normalizes proposed facts to canonical concepts) and at query time (concept expansion widens
/// the embedding query to the full alias family). See
/// docs/backlog/Normalized-Lexicon-for-Term-Resolution.md.
/// </summary>
public sealed class LexiconConcept
{
    /// <summary>Canonical concept key, e.g. <c>due_date</c>. Stable identifier used in facts and queries.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Human-readable label, e.g. "Due date".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Surface forms that all mean this concept, e.g. "bid due", "proposal due date", "deadline".</summary>
    public IReadOnlyList<string> Aliases { get; set; } = Array.Empty<string>();

    /// <summary>Value pattern used by the fidelity gate: <c>date</c>, <c>currency</c>, <c>number</c>, <c>text</c>, or null (text).</summary>
    public string? ValuePattern { get; set; }

    /// <summary>Optional canonical template name this concept is scoped to (null = global).</summary>
    public string? TemplateScope { get; set; }
}
