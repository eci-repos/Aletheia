using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.Lexicon;

namespace RAGS.UnitTests;

public class FactVerifierTests
{
    private static readonly IReadOnlyList<LexiconConcept> Concepts = new[]
    {
        new LexiconConcept
        {
            Key = "due_date",
            Label = "Due date",
            ValuePattern = "date",
            Aliases = new[] { "due date", "bid due", "proposal due date" }
        }
    };

    [Fact]
    public void Verify_keeps_fact_when_span_exists_and_value_parses()
    {
        const string text = "Proposal Due Date: February 24, 2022, at 2:00 p.m. EST";
        var proposals = new[]
        {
            new ProposedFact
            {
                ConceptHint = "due_date",
                Value = "February 24, 2022",
                SourceSpan = "Proposal Due Date: February 24, 2022, at 2:00 p.m. EST"
            }
        };

        var facts = FactVerifier.Verify(proposals, text, null, Concepts);

        var fact = Assert.Single(facts);
        Assert.Equal("due_date", fact.ConceptKey);
        Assert.Equal("2022-02-24", fact.Value);
        Assert.Null(fact.PageNumber);
    }

    [Fact]
    public void Verify_drops_fact_when_span_not_in_source()
    {
        const string text = "Some unrelated text.";
        var proposals = new[]
        {
            new ProposedFact
            {
                ConceptHint = "due_date",
                Value = "February 24, 2022",
                SourceSpan = "Proposal Due Date: February 24, 2022"
            }
        };

        var facts = FactVerifier.Verify(proposals, text, null, Concepts);

        Assert.Empty(facts);
    }

    [Fact]
    public void Verify_drops_fact_when_value_does_not_parse()
    {
        const string text = "Proposal Due Date: February 24, 2022";
        var proposals = new[]
        {
            new ProposedFact
            {
                ConceptHint = "due_date",
                Value = "not a date",
                SourceSpan = "Proposal Due Date: February 24, 2022"
            }
        };

        var facts = FactVerifier.Verify(proposals, text, null, Concepts);

        Assert.Empty(facts);
    }

    [Fact]
    public void Verify_resolves_concept_by_alias()
    {
        const string text = "Bid due: August 26, 2026, 2:00 PM Pacific Time";
        var proposals = new[]
        {
            new ProposedFact
            {
                ConceptHint = "bid due",
                Value = "August 26, 2026",
                SourceSpan = "Bid due: August 26, 2026, 2:00 PM Pacific Time"
            }
        };

        var facts = FactVerifier.Verify(proposals, text, null, Concepts);

        var fact = Assert.Single(facts);
        Assert.Equal("due_date", fact.ConceptKey);
        Assert.Equal("2026-08-26", fact.Value);
    }

    [Fact]
    public void Verify_anchors_fact_to_page_and_offset()
    {
        const string text = "Page one content.\nProposal Due Date: February 24, 2022\nPage two content.";
        var pages = new[]
        {
            new TextPage(1, 0, "Page one content.".Length),
            new TextPage(2, "Page one content.\n".Length, text.Length - "Page one content.\n".Length)
        };
        var proposals = new[]
        {
            new ProposedFact
            {
                ConceptHint = "due_date",
                Value = "February 24, 2022",
                SourceSpan = "Proposal Due Date: February 24, 2022"
            }
        };

        var facts = FactVerifier.Verify(proposals, text, pages, Concepts);

        var fact = Assert.Single(facts);
        Assert.Equal(2, fact.PageNumber);
        Assert.Equal(0, fact.OffsetInPage);
    }

    [Fact]
    public void Verify_matches_span_across_line_breaks()
    {
        const string text = "Proposal Due Date:\nFebruary 24, 2022";
        var proposals = new[]
        {
            new ProposedFact
            {
                ConceptHint = "due_date",
                Value = "February 24, 2022",
                SourceSpan = "Proposal Due Date: February 24, 2022"
            }
        };

        var facts = FactVerifier.Verify(proposals, text, null, Concepts);

        Assert.Single(facts);
    }

    [Fact]
    public void Verify_keeps_fact_with_unmapped_concept_as_text_value()
    {
        const string text = "Novel term: some value";
        var proposals = new[]
        {
            new ProposedFact
            {
                ConceptHint = "novel_concept",
                Value = "some value",
                SourceSpan = "Novel term: some value"
            }
        };

        var facts = FactVerifier.Verify(proposals, text, null, Concepts);

        var fact = Assert.Single(facts);
        Assert.Equal("novel_concept", fact.ConceptKey);
        Assert.Equal("some value", fact.Value);
    }

    // ---- Sprint 71: template_scope enforcement ----

    private static readonly IReadOnlyList<LexiconConcept> ScopedConcepts = new[]
    {
        new LexiconConcept
        {
            Key = "due_date",
            Label = "Due date",
            ValuePattern = "date",
            TemplateScope = "RFP",
            Aliases = new[] { "due date", "proposal due date" }
        },
        new LexiconConcept
        {
            Key = "budget",
            Label = "Budget",
            ValuePattern = "currency",
            Aliases = new[] { "budget" }
        }
    };

    private static ProposedFact DueDateProposal() => new()
    {
        ConceptHint = "due_date",
        Value = "February 24, 2022",
        SourceSpan = "Proposal Due Date: February 24, 2022"
    };

    [Fact]
    public void Verify_applies_scoped_concept_when_template_matches()
    {
        const string text = "Proposal Due Date: February 24, 2022";

        var facts = FactVerifier.Verify(new[] { DueDateProposal() }, text, null, ScopedConcepts, "RFP");

        var fact = Assert.Single(facts);
        Assert.Equal("due_date", fact.ConceptKey);
    }

    [Fact]
    public void Verify_treats_out_of_scope_concept_as_unmapped_text()
    {
        const string text = "Proposal Due Date: February 24, 2022";

        // The scoped concept is excluded from matching, so the hint behaves like an unknown hint:
        // the verifiable value is still stored, but as raw text under the raw hint (not normalized
        // to the concept's date pattern). The hint is surfaced to the admin via the unmapped queue.
        var facts = FactVerifier.Verify(new[] { DueDateProposal() }, text, null, ScopedConcepts, "Contract");

        var fact = Assert.Single(facts);
        Assert.Equal("due_date", fact.ConceptKey);
        Assert.Equal("February 24, 2022", fact.Value);
    }

    [Fact]
    public void Verify_treats_scoped_concept_as_unmapped_text_when_no_template()
    {
        const string text = "Proposal Due Date: February 24, 2022";

        var facts = FactVerifier.Verify(new[] { DueDateProposal() }, text, null, ScopedConcepts);

        var fact = Assert.Single(facts);
        Assert.Equal("due_date", fact.ConceptKey);
        Assert.Equal("February 24, 2022", fact.Value);
    }

    [Fact]
    public void Verify_keeps_unscoped_concept_for_any_template()
    {
        const string text = "Budget: $1,000,000";
        var proposals = new[]
        {
            new ProposedFact
            {
                ConceptHint = "budget",
                Value = "$1,000,000",
                SourceSpan = "Budget: $1,000,000"
            }
        };

        var facts = FactVerifier.Verify(proposals, text, null, ScopedConcepts, "Contract");

        var fact = Assert.Single(facts);
        Assert.Equal("budget", fact.ConceptKey);
    }
}
