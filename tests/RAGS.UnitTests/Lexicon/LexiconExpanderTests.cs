using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.Lexicon;

namespace RAGS.UnitTests;

public class LexiconExpanderTests
{
    private static readonly IReadOnlyList<LexiconConcept> Concepts = new[]
    {
        new LexiconConcept
        {
            Key = "due_date",
            Label = "Due date",
            Aliases = new[] { "due date", "bid due", "proposal due date", "submission due date", "deadline" }
        }
    };

    [Fact]
    public void Expand_appends_concept_label_and_alias_family_when_query_mentions_an_alias()
    {
        var expanded = LexiconExpander.Expand("What is the submission due date for the CMP 2026 RFP?", Concepts);

        Assert.Contains("submission due date", expanded);
        Assert.Contains("Due date", expanded);
        Assert.Contains("bid due", expanded);
        Assert.Contains("proposal due date", expanded);
        Assert.Contains("deadline", expanded);
    }

    [Fact]
    public void Expand_keeps_the_original_query()
    {
        var query = "What is the submission due date for the CMP 2026 RFP?";
        var expanded = LexiconExpander.Expand(query, Concepts);

        Assert.StartsWith(query, expanded);
    }

    [Fact]
    public void Expand_is_noop_when_no_alias_matches()
    {
        var query = "What is the weather today?";
        Assert.Equal(query, LexiconExpander.Expand(query, Concepts));
    }

    [Fact]
    public void Expand_is_noop_for_empty_query_or_concepts()
    {
        Assert.Equal(string.Empty, LexiconExpander.Expand(string.Empty, Concepts));
        Assert.Equal("hello", LexiconExpander.Expand("hello", Array.Empty<LexiconConcept>()));
        Assert.Equal("hello", LexiconExpander.Expand("hello", null!));
    }

    [Fact]
    public void Expand_matches_aliases_case_insensitively()
    {
        var expanded = LexiconExpander.Expand("BID DUE date?", Concepts);

        Assert.Contains("bid due", expanded);
    }

    [Fact]
    public void Expand_does_not_expand_inside_a_longer_word()
    {
        // "deadline" is an alias; "deadlines" must not trigger a partial-word match.
        var expanded = LexiconExpander.Expand("The deadlines are firm.", Concepts);

        Assert.Equal("The deadlines are firm.", expanded);
    }
}
