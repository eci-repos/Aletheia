using Aletheia.Repository.API.Services;

namespace Repository.UnitTests.Services;

public class IngestionDiagnosticsTests
{
    [Fact]
    public void Template_gate_skips_are_counted_and_recorded()
    {
        var diagnostics = new IngestionDiagnostics();

        diagnostics.RecordTemplateGateSkip("Q3 Report.xlsx");
        diagnostics.RecordTemplateGateSkip("Q3 Report.xlsx");

        Assert.Equal(2, diagnostics.TemplateGateSkipCount);
        Assert.Equal(2, diagnostics.TemplateGateSkips.Count);
        Assert.Contains("Q3 Report.xlsx", diagnostics.TemplateGateSkips);
        Assert.Equal(0, diagnostics.ExtractionFailureCount);
    }

    [Fact]
    public void Extraction_failures_are_counted()
    {
        var diagnostics = new IngestionDiagnostics();

        diagnostics.RecordExtractionFailure("broken.docx", "no text");

        Assert.Equal(1, diagnostics.ExtractionFailureCount);
        Assert.Contains("broken.docx (no text)", diagnostics.TemplateGateSkips);
        Assert.Equal(0, diagnostics.TemplateGateSkipCount);
    }

    [Fact]
    public void Recent_skips_are_bounded()
    {
        var diagnostics = new IngestionDiagnostics();

        for (var i = 0; i < 120; i++)
        {
            diagnostics.RecordTemplateGateSkip($"doc-{i}.pdf");
        }

        Assert.Equal(120, diagnostics.TemplateGateSkipCount);
        Assert.True(diagnostics.TemplateGateSkips.Count <= 50);
    }
}
