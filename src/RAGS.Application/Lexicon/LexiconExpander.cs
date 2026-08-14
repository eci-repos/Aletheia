using System.Text.RegularExpressions;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Application.Lexicon;

/// <summary>
/// Query-time concept expansion: when a query mentions a lexicon concept (via any of its aliases),
/// the concept's label and full alias family are appended to the embedding query so a short or
/// varied phrasing ("submission due date") retrieves documents that use any surface form ("Bid
/// due", "Proposal Due Date", "deadline"). The original query is always kept. Applied after
/// <c>QueryExpander</c> (acronyms); the keyword fallback keeps the original query because it is a
/// whole-string ILIKE match.
/// </summary>
public static class LexiconExpander
{
    public static string Expand(string query, IReadOnlyList<LexiconConcept> concepts)
    {
        if (string.IsNullOrWhiteSpace(query) || concepts is null || concepts.Count == 0)
        {
            return query;
        }

        var expanded = query;
        foreach (var concept in concepts)
        {
            if (concept.Aliases is null || concept.Aliases.Count == 0)
            {
                continue;
            }

            if (!BuildAliasRegex(concept.Aliases).IsMatch(query))
            {
                continue;
            }

            var additions = new List<string>(concept.Aliases.Count + 1) { concept.Label };
            additions.AddRange(concept.Aliases);
            expanded = $"{expanded} {string.Join(" ", additions.Distinct(StringComparer.OrdinalIgnoreCase))}";
        }

        return expanded;
    }

    private static Regex BuildAliasRegex(IReadOnlyList<string> aliases)
    {
        // Longest-first alternation so "submission due date" wins over "due date" at the same position.
        var pattern = string.Join("|", aliases.OrderByDescending(a => a.Length).Select(Regex.Escape));
        return new Regex($@"\b({pattern})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }
}
