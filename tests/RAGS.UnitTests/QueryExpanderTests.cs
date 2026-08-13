using Aletheia.RAGS.Application;

namespace RAGS.UnitTests;

public class QueryExpanderTests
{
    [Fact]
    public void Expand_keeps_original_token_and_appends_expansion()
    {
        var expanded = QueryExpander.Expand("list AI required features");

        Assert.Contains("AI Artificial Intelligence", expanded, StringComparison.Ordinal);
        Assert.StartsWith("list AI Artificial Intelligence required features", expanded, StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_is_case_insensitive()
    {
        var expanded = QueryExpander.Expand("ai features");

        Assert.Contains("ai Artificial Intelligence", expanded, StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_does_not_expand_inside_longer_words()
    {
        var expanded = QueryExpander.Expand("email said aim training");

        Assert.Equal("email said aim training", expanded);
    }

    [Fact]
    public void Expand_handles_multiple_acronyms_in_one_query()
    {
        var expanded = QueryExpander.Expand("AI RFP opportunities");

        Assert.Contains("AI Artificial Intelligence", expanded, StringComparison.Ordinal);
        Assert.Contains("RFP Request for Proposal", expanded, StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_prefers_longest_acronym_at_same_position()
    {
        var expanded = QueryExpander.Expand("GenAI disclosure");

        // "GenAI" wins over "AI"; the expansion's own "AI" is not re-expanded (single pass).
        Assert.Equal("GenAI Generative AI disclosure", expanded);
    }

    [Fact]
    public void Expand_returns_unchanged_for_null_or_whitespace()
    {
        Assert.Null(QueryExpander.Expand(null!));
        Assert.Equal(string.Empty, QueryExpander.Expand(string.Empty));
        Assert.Equal("   ", QueryExpander.Expand("   "));
    }

    [Fact]
    public void Expand_leaves_queries_without_acronyms_unchanged()
    {
        const string query = "what is the project budget";
        Assert.Equal(query, QueryExpander.Expand(query));
    }
}
