namespace Aletheia.RAGS.Abstractions.Models;

/// <summary>
/// A concept hint proposed by the semantic layer that matched no known lexicon concept or alias.
/// Collected per source so an admin can review and add it to the lexicon — the governance loop
/// that lets new documents' vocabularies be absorbed instead of missed.
/// </summary>
public sealed class UnmappedTerm
{
    public string Term { get; set; } = string.Empty;

    public Guid SourceId { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary><c>pending</c> (awaiting admin review) or <c>resolved</c> (confirmed as an alias or dismissed).</summary>
    public string Status { get; set; } = "pending";

    public DateTime? ResolvedAt { get; set; }
}
