using Aletheia.RAGS.Application;
using Xunit;

namespace RAGS.UnitTests;

public sealed class DocumentTemplateRegistryTests
{
    [Fact]
    public void TryGetSections_matches_document_by_template_name()
    {
        var registry = new DocumentTemplateRegistry();

        var sections = registry.TryGetSections("CMP 2026 - 3. RFP Analysis.docx");

        Assert.NotNull(sections);
        Assert.NotEmpty(sections);
        Assert.Contains(sections, section => section.Title.Contains("Project Summary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryGetSections_matches_any_document_using_the_same_template()
    {
        var registry = new DocumentTemplateRegistry();

        var sections2022 = registry.TryGetSections("CMP 2022 - 3. RFP Analysis.docx");
        var sections2026 = registry.TryGetSections("CMP 2026 - 3. RFP Analysis.docx");

        Assert.NotNull(sections2022);
        Assert.NotNull(sections2026);
        Assert.Equal(sections2022!.Count, sections2026!.Count);
        for (var i = 0; i < sections2022.Count; i++)
        {
            Assert.Equal(sections2022[i].Title, sections2026[i].Title);
        }
    }

    [Fact]
    public void TryGetCanonicalName_returns_template_name()
    {
        var registry = new DocumentTemplateRegistry();

        Assert.Equal("3.0 - RFP Analysis", registry.TryGetCanonicalName("CMP 2026 - 3. RFP Analysis.docx"));
    }

    [Fact]
    public void TryGetCanonicalName_returns_null_for_unmatched_document()
    {
        var registry = new DocumentTemplateRegistry();

        Assert.Null(registry.TryGetCanonicalName("Q3 Financial Report.xlsx"));
    }

    [Fact]
    public void TryGetSections_returns_null_for_unmatched_document()
    {
        var registry = new DocumentTemplateRegistry();

        Assert.Null(registry.TryGetSections("Q3 Financial Report.xlsx"));
    }

    [Fact]
    public void Sections_are_ordered_and_described()
    {
        var registry = new DocumentTemplateRegistry();

        var sections = registry.TryGetSections("CMP 2026 - 3. RFP Analysis.docx");

        Assert.NotNull(sections);
        var projectSummary = sections!.FirstOrDefault(section => section.Title.Contains("Project Summary", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(projectSummary);
        Assert.False(string.IsNullOrWhiteSpace(projectSummary!.Description));
    }
}
