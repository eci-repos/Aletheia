namespace Aletheia.RAGS.Abstractions.Models;

/// <summary>
/// A fact proposed by the semantic (LLM) layer before the fidelity gate runs. The proposer is
/// instructed to quote the exact source span it read the value from; the verifier confirms the
/// span exists in the extracted text and the value parses before anything is stored.
/// </summary>
public sealed class ProposedFact
{
    /// <summary>Concept key or alias the proposer believes fits, e.g. <c>due_date</c>.</summary>
    public string ConceptHint { get; set; } = string.Empty;

    /// <summary>The fact's value as written in the text, e.g. "February 24, 2022".</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>The exact contiguous source text supporting this fact, quoted verbatim.</summary>
    public string SourceSpan { get; set; } = string.Empty;
}
