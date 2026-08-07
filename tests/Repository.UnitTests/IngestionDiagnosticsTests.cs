using Aletheia.Repository.API.Services;

namespace Repository.UnitTests.Services;

public class IngestionDiagnosticsTests
{
    [Fact]
    public void Uncategorized_ingests_are_counted_and_recorded()
    {
        var diagnostics = new IngestionDiagnostics();

        diagnostics.RecordUncategorizedIngest("Q3 Report.xlsx");
        diagnostics.RecordUncategorizedIngest("Q3 Report.xlsx");

        Assert.Equal(2, diagnostics.UncategorizedIngestCount);
        Assert.Equal(2, diagnostics.UncategorizedIngests.Count);
        Assert.Contains("Q3 Report.xlsx", diagnostics.UncategorizedIngests);
        Assert.Equal(0, diagnostics.ExtractionFailureCount);
    }

    [Fact]
    public void Extraction_failures_are_counted()
    {
        var diagnostics = new IngestionDiagnostics();

        diagnostics.RecordExtractionFailure("broken.docx", "no text");

        Assert.Equal(1, diagnostics.ExtractionFailureCount);
        Assert.Contains("broken.docx (no text)", diagnostics.UncategorizedIngests);
        Assert.Equal(0, diagnostics.UncategorizedIngestCount);
    }

    [Fact]
    public void Recent_ingests_are_bounded()
    {
        var diagnostics = new IngestionDiagnostics();

        for (var i = 0; i < 120; i++)
        {
            diagnostics.RecordUncategorizedIngest($"doc-{i}.pdf");
        }

        Assert.Equal(120, diagnostics.UncategorizedIngestCount);
        Assert.True(diagnostics.UncategorizedIngests.Count <= 50);
    }
}
