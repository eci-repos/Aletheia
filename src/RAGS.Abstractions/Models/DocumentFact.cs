namespace Aletheia.RAGS.Abstractions.Models;

/// <summary>
/// A verified, normalized fact extracted from a source document. Only facts that passed the
/// fidelity gate (the source span exists in the extracted text and the value parses against the
/// concept's value pattern) are persisted — nothing enters the knowledge base that is not in the
/// source. Page/offset anchor the fact to the exact passage (Sprint 67 viewer machinery).
/// </summary>
public sealed class DocumentFact
{
    public Guid SourceId { get; set; }

    /// <summary>Canonical lexicon concept key, e.g. <c>due_date</c>.</summary>
    public string ConceptKey { get; set; } = string.Empty;

    /// <summary>Normalized value, e.g. "2022-02-24" for a date or "1200000" for a currency.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>The exact source passage that supports this fact, as quoted by the proposer.</summary>
    public string SourceSpan { get; set; } = string.Empty;

    public int? PageNumber { get; set; }

    public int? OffsetInPage { get; set; }

    /// <summary><c>verified</c> today; reserved for <c>flagged</c> when the fidelity gate is relaxed.</summary>
    public string Status { get; set; } = "verified";
}
