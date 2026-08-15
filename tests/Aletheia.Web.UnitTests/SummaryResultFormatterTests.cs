using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Web.Services;

namespace Aletheia.Web.UnitTests;

/// <summary>
/// Guards Sprint 73 display formatting for Summaries search results. GraphRAG summary candidates
/// carry internal scaffolding in <see cref="Chunk.Content"/> that is meaningless to end users; the
/// formatter extracts the readable body and decides which card affordances apply. The backend
/// content is left untouched.
/// </summary>
public sealed class SummaryResultFormatterTests
{
    [Theory]
    [InlineData("summary-entity")]
    [InlineData("summary-community")]
    [InlineData("Summary-Entity")]
    public void IsSummary_true_for_graphrag_summary_strategies(string strategy)
    {
        var result = CreateResult(strategy, "Entity Summary: CMP 2026\nBody text.");

        Assert.True(SummaryResultFormatter.IsSummary(result));
    }

    [Theory]
    [InlineData("lazy-entity")]
    [InlineData("lazy-community")]
    [InlineData("semantic")]
    [InlineData("semantic-timeout-fallback")]
    [InlineData("")]
    public void IsSummary_false_for_real_passages_and_fallbacks(string strategy)
    {
        var result = CreateResult(strategy, "A real passage from a document.");

        Assert.False(SummaryResultFormatter.IsSummary(result));
    }

    [Fact]
    public void IsSummary_false_for_null_result()
    {
        Assert.False(SummaryResultFormatter.IsSummary(null!));
    }

    [Fact]
    public void Body_strips_entity_summary_prefix_and_structured_context_dump()
    {
        const string content =
            "Entity Summary: CMP 2026\n" +
            "The RFP sets a submission due date of February 24, 2022.\n\n" +
            "Structured GraphRAG Context\n" +
            "Entity: CMP 2026\n" +
            "Relationships: ...";

        var body = SummaryResultFormatter.Body(content);

        Assert.Equal("The RFP sets a submission due date of February 24, 2022.", body);
    }

    [Fact]
    public void Body_strips_community_summary_prefix_and_structured_context_dump()
    {
        const string content =
            "Community Summary: Procurement\n" +
            "Multiple RFPs share a common submission deadline pattern.\n\n" +
            "Structured GraphRAG Context\n" +
            "Community: Procurement\n" +
            "Members: ...";

        var body = SummaryResultFormatter.Body(content);

        Assert.Equal("Multiple RFPs share a common submission deadline pattern.", body);
    }

    [Fact]
    public void Body_returns_trimmed_content_when_no_scaffolding_present()
    {
        const string content = "  A plain summary body with no prefix or dump.  ";

        var body = SummaryResultFormatter.Body(content);

        Assert.Equal("A plain summary body with no prefix or dump.", body);
    }

    [Fact]
    public void Body_returns_empty_for_blank_content()
    {
        Assert.Equal(string.Empty, SummaryResultFormatter.Body("   "));
        Assert.Equal(string.Empty, SummaryResultFormatter.Body(null!));
    }

    [Fact]
    public void ShowViewInDocument_false_for_summaries()
    {
        var summary = CreateResult("summary-community", "Community Summary: X\nBody.");

        Assert.False(SummaryResultFormatter.ShowViewInDocument(summary));
    }

    [Fact]
    public void ShowViewInDocument_true_for_semantic_passages()
    {
        var passage = CreateResult("semantic", "A real passage.");

        Assert.True(SummaryResultFormatter.ShowViewInDocument(passage));
    }

    private static SearchResult CreateResult(string strategy, string content)
    {
        return new SearchResult(
            new Chunk(Guid.NewGuid(), Guid.NewGuid(), content, 0),
            score: 0.9f,
            citations: new[] { "CMP 2026 - 3. RFP Analysis.docx" },
            retrievalStrategy: strategy);
    }
}
