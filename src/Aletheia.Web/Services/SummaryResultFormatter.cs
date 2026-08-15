using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.Web.Services;

/// <summary>
/// Sprint 73: display formatting for Summaries search results. GraphRAG summary candidates carry
/// internal scaffolding in <see cref="Chunk.Content"/> — an "Entity Summary: {label}" /
/// "Community Summary: {name}" prefix line plus a "Structured GraphRAG Context" dump — that is
/// meaningless to end users. This helper extracts the readable summary body and decides which card
/// affordances apply. The backend content is left untouched ("internally they can stay as they are").
/// </summary>
public static class SummaryResultFormatter
{
    private const string StructuredContextMarker = "Structured GraphRAG Context";

    /// <summary>True when the result is a synthesized GraphRAG summary (retrieval strategy
    /// "summary-entity" / "summary-community"). LazyGraphRAG fallback results ("lazy-*") are real
    /// passages and keep the standard semantic card treatment.</summary>
    public static bool IsSummary(SearchResult result)
    {
        return result is not null
            && !string.IsNullOrWhiteSpace(result.RetrievalStrategy)
            && result.RetrievalStrategy.StartsWith("summary-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Extracts the readable summary body: strips the "Entity Summary: X" / "Community
    /// Summary: X" prefix line and the trailing structured-context dump, then trims.</summary>
    public static string Body(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var body = content;
        var firstLineBreak = body.IndexOf('\n');
        if (firstLineBreak > 0)
        {
            body = body[(firstLineBreak + 1)..];
        }

        var contextIndex = body.IndexOf(StructuredContextMarker, StringComparison.Ordinal);
        if (contextIndex > 0)
        {
            body = body[..contextIndex];
        }

        return body.Trim();
    }

    /// <summary>A synthesized summary has no single verbatim passage in a document, so the
    /// "View in document" highlight link is dead for it (community summaries even carry a synthetic
    /// source id). Hide the button and let the Sources list carry the provenance.</summary>
    public static bool ShowViewInDocument(SearchResult result) => !IsSummary(result);
}
