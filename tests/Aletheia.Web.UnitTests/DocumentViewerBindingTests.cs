namespace Aletheia.Web.UnitTests;

public class DocumentViewerBindingTests
{
    [Fact]
    public void Document_viewer_has_route_and_accepts_page_chunk_version_params()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Document/View.razor"));

        Assert.Contains("@page \"/document/{id}\"", source);
        Assert.Contains("public int? Page", source);
        Assert.Contains("public string? Chunk", source);
        Assert.Contains("public string? Version", source);
    }

    [Fact]
    public void Document_viewer_renders_pdf_via_pdfjs_and_text_with_page_markers()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Document/View.razor"));

        Assert.Contains("renderPdf", source);
        Assert.Contains("pdf-viewer", source);
        Assert.Contains("text-page", source);
        Assert.Contains("Page @textPage.PageNumber", source);
    }

    [Fact]
    public void Document_viewer_highlights_passage_and_auto_scrolls()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Document/View.razor"));

        Assert.Contains("passage-highlight", source);
        Assert.Contains("scrollToDocumentElement", source);
        Assert.Contains("HighlightPhrase", source);
    }

    [Fact]
    public void Document_viewer_css_defines_pdf_and_text_styles()
    {
        var css = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Document/View.razor.css"));

        Assert.Contains(".pdf-viewer", css);
        Assert.Contains(".pdf-text-layer", css);
        Assert.Contains(".pdf-highlight", css);
        Assert.Contains(".text-page", css);
        Assert.Contains(".passage-highlight", css);
    }

    [Fact]
    public void Index_html_loads_pdfjs_and_defines_renderPdf()
    {
        var html = File.ReadAllText(FindRepoFile("src/Aletheia.Web/wwwroot/index.html"));

        Assert.Contains("pdfjs-dist", html);
        Assert.Contains("window.renderPdf", html);
        Assert.Contains("window.scrollToDocumentElement", html);
    }

    [Fact]
    public void Search_center_result_cards_link_to_document_viewer()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/SearchCenter.razor"));

        Assert.Contains("View in document", source);
        Assert.Contains("document/@result.Chunk.SourceId", source);
        Assert.Contains("LeadingPhrase", source);
    }

    [Fact]
    public void Copilot_citations_become_viewer_links()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Pages/Copilot/Index.razor"));

        Assert.Contains("LinkCitations", source);
        Assert.Contains("copilot-citation", source);
        Assert.Contains("msg.Citations", source);
    }

    [Fact]
    public void RepositoryApiClient_has_preview_method()
    {
        var source = File.ReadAllText(FindRepoFile("src/Aletheia.Web/Services/RepositoryApiClient.cs"));

        Assert.Contains("PreviewAsync", source);
        Assert.Contains("FilePreviewClientResult", source);
        Assert.Contains("FileTextPreviewClientResponse", source);
    }

    private static string FindRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}.");
    }
}
