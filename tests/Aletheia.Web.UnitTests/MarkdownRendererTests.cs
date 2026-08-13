using Aletheia.Web.Services;

namespace Aletheia.Web.UnitTests;

public class MarkdownRendererTests
{
    [Fact]
    public void ToHtml_empty_null_and_whitespace_yield_empty_string()
    {
        Assert.Equal(string.Empty, MarkdownRenderer.ToHtml(string.Empty));
        Assert.Equal(string.Empty, MarkdownRenderer.ToHtml(null!));
        Assert.Equal(string.Empty, MarkdownRenderer.ToHtml("   "));
    }

    [Theory]
    [InlineData("# Title", "<h1>Title</h1>")]
    [InlineData("## Sub", "<h2>Sub</h2>")]
    [InlineData("### Sub", "<h3>Sub</h3>")]
    [InlineData("#### Sub", "<h4>Sub</h4>")]
    public void ToHtml_renders_headings(string markdown, string expected)
    {
        Assert.Equal(expected, MarkdownRenderer.ToHtml(markdown));
    }

    [Fact]
    public void ToHtml_renders_paragraphs()
    {
        Assert.Equal("<p>Plain text</p>", MarkdownRenderer.ToHtml("Plain text"));
    }

    [Fact]
    public void ToHtml_renders_lists()
    {
        Assert.Equal("<ul><li>One</li><li>Two</li></ul>", MarkdownRenderer.ToHtml("- One\n- Two"));
        Assert.Equal("<ul><li>Star</li></ul>", MarkdownRenderer.ToHtml("* Star"));
    }

    [Fact]
    public void ToHtml_renders_tables()
    {
        const string markdown = "| A | B |\n|---|---|\n| 1 | 2 |";
        var html = MarkdownRenderer.ToHtml(markdown);

        Assert.Contains("md-table-wrap", html);
        Assert.Contains("<th>A</th>", html);
        Assert.Contains("<th>B</th>", html);
        Assert.Contains("<td>1</td>", html);
        Assert.Contains("<td>2</td>", html);
    }

    [Fact]
    public void ToHtml_renders_inline_bold_and_code()
    {
        Assert.Equal("<p><strong>bold</strong></p>", MarkdownRenderer.ToHtml("**bold**"));
        Assert.Equal("<p>Use <code>var</code> here</p>", MarkdownRenderer.ToHtml("Use `var` here"));
    }

    [Fact]
    public void ToHtml_escapes_raw_html_never_emits_markup()
    {
        var html = MarkdownRenderer.ToHtml("Hello <script>alert(1)</script>");

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void ToHtml_handles_crlf_line_endings()
    {
        Assert.Equal("<p>One</p><p>Two</p>", MarkdownRenderer.ToHtml("One\r\n\r\nTwo"));
    }
}
