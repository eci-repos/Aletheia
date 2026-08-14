namespace Aletheia.RAGS.Abstractions.Models;

/// <summary>
/// The canonical default lexicon concepts. The SQL seed in <c>scripts/init.sql</c> and the
/// migration <c>2026-08-14-lexicon-and-facts.sql</c> mirror these rows (a binding test keeps them
/// in sync); this class is the C# reference used by tests and as documentation of the baseline.
/// Extend here (and in the SQL seed) as new concepts appear in the corpus.
/// </summary>
public static class LexiconSeedData
{
    public static readonly IReadOnlyList<LexiconConcept> Defaults = new LexiconConcept[]
    {
        new()
        {
            Key = "due_date",
            Label = "Due date",
            ValuePattern = "date",
            Aliases = new[]
            {
                "due date", "bid due", "proposal due date", "submission due date", "deadline",
                "closing date", "submission deadline", "response due", "bid deadline", "proposal deadline"
            }
        },
        new()
        {
            Key = "budget",
            Label = "Budget",
            ValuePattern = "currency",
            Aliases = new[]
            {
                "budget", "total budget", "funding amount", "contract value", "award amount",
                "maximum amount", "ceiling"
            }
        },
        new()
        {
            Key = "page_limit",
            Label = "Page limit",
            ValuePattern = "number",
            Aliases = new[] { "page limit", "maximum pages", "page count", "not to exceed" }
        },
        new()
        {
            Key = "vendor",
            Label = "Vendor",
            ValuePattern = "text",
            Aliases = new[] { "vendor", "contractor", "supplier", "offeror", "bidder", "proposer" }
        },
        new()
        {
            Key = "submission",
            Label = "Submission",
            ValuePattern = "text",
            Aliases = new[] { "submission", "proposal", "bid", "offer", "response" }
        }
    };
}
