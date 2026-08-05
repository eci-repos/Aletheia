using System.Text.RegularExpressions;

namespace Aletheia.RAGS.Abstractions.Models;

public static class KnowledgeTermNormalizer
{
    private static readonly IReadOnlyDictionary<string, string> Acronyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ai"] = "AI",
        ["api"] = "API",
        ["bm25"] = "BM25",
        ["cmp"] = "CMP",
        ["llm"] = "LLM",
        ["rag"] = "RAG",
        ["rags"] = "RAGS",
        ["rfp"] = "RFP",
        ["rpf"] = "RFP",
        ["tf-idf"] = "TF-IDF",
        ["wrags"] = "WRAGS"
    };

    public static string NormalizeLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = Regex.Replace(value.Trim(), @"\s+", " ");
        if (Acronyms.TryGetValue(trimmed, out var acronym))
        {
            return acronym;
        }

        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    public static IReadOnlyList<string> GetLookupAliases(string value)
    {
        var normalized = NormalizeLabel(value);
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(value))
        {
            aliases.Add(value.Trim());
        }

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            aliases.Add(normalized);
        }

        if (string.Equals(normalized, "RFP", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("Rfp");
            aliases.Add("Rpf");
            aliases.Add("rfp");
            aliases.Add("rpf");
        }

        return aliases.ToList();
    }
}
