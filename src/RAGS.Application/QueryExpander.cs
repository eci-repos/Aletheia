using System.Text.RegularExpressions;

namespace Aletheia.RAGS.Application;

/// <summary>
/// Expands domain acronyms in a user query before embedding so that a short acronym ("AI") retrieves
/// documents that spell it out ("Artificial Intelligence", "Generative AI"). The original token is
/// always kept, so the literal acronym still matches. Single-pass, word-boundary aware, case-insensitive;
/// never expands inside a longer word ("email", "AIM"). Applied to the embedding query only — the keyword
/// fallback keeps the original query because it is a whole-string ILIKE match.
/// </summary>
public static class QueryExpander
{
    /// <summary>Domain acronym → expansion. Extend here as new acronyms appear in the corpus.</summary>
    public static readonly IReadOnlyDictionary<string, string> Expansions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["AI"] = "Artificial Intelligence",
        ["GenAI"] = "Generative AI",
        ["RFP"] = "Request for Proposal",
        ["RFI"] = "Request for Information",
        ["ML"] = "Machine Learning",
        ["LLM"] = "Large Language Model",
        ["NLP"] = "Natural Language Processing",
        ["API"] = "Application Programming Interface",
        ["SOW"] = "Statement of Work",
        ["SLA"] = "Service Level Agreement",
        ["KPI"] = "Key Performance Indicator",
        ["POC"] = "Proof of Concept",
        ["MVP"] = "Minimum Viable Product",
        ["OCR"] = "Optical Character Recognition",
        ["PDF"] = "Portable Document Format",
        ["SQL"] = "Structured Query Language",
        ["RAG"] = "Retrieval Augmented Generation"
    };

    private static readonly Regex ExpansionRegex = BuildExpansionRegex();

    public static string Expand(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return query;
        }

        return ExpansionRegex.Replace(query, match =>
            Expansions.TryGetValue(match.Value, out var expansion)
                ? $"{match.Value} {expansion}"
                : match.Value);
    }

    private static Regex BuildExpansionRegex()
    {
        // Longest-first alternation so "GenAI" wins over "AI" at the same position; single pass means
        // an expansion's own text ("Generative AI") is never re-scanned.
        var pattern = string.Join("|", Expansions.Keys.OrderByDescending(key => key.Length).Select(Regex.Escape));
        return new Regex($@"\b({pattern})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }
}
