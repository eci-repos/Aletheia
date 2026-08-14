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
            Aliases = new[] { "due date", "bid due", "proposal due date", "submission due date", "deadline", "end date" }
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
    public void Expand_matches_plural_form_of_an_alias()
    {
        // "deadline" is an alias; "deadlines" (its plural) must trigger expansion.
        var expanded = LexiconExpander.Expand("The deadlines are firm.", Concepts);

        Assert.Contains("deadline", expanded);
    }

    [Fact]
    public void Expand_matches_plural_form_of_a_multi_word_alias()
    {
        // The user-reported case: "end dates" must map to the "end date" alias of due_date.
        var expanded = LexiconExpander.Expand("list the end dates of RFPs", Concepts);

        Assert.Contains("end date", expanded);
        Assert.Contains("Due date", expanded);
    }

    [Fact]
    public void Expand_does_not_expand_inside_a_longer_non_plural_word()
    {
        // "deadline" is an alias; "deadlined" is neither the alias nor its plural.
        var expanded = LexiconExpander.Expand("The project was deadlined.", Concepts);

        Assert.Equal("The project was deadlined.", expanded);
    }
}
