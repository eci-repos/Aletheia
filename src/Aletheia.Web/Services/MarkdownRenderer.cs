using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Aletheia.Web.Services;

/// <summary>
/// Renders a small, safe subset of markdown (headings, pipe tables, lists,
/// paragraphs, inline bold and inline code) to HTML. All text is HTML-encoded
/// before any formatting is applied, so raw HTML in source content is escaped,
/// never emitted as markup. This is the single markdown renderer for the Web
/// surface — shared by Copilot chat messages and Wiki page summaries.
/// </summary>
public static class MarkdownRenderer
{
    public static string ToHtml(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var lines = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var html = new StringBuilder();

        for (var i = 0; i < lines.Length;)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            if (IsTableStart(lines, i))
            {
                i = AppendTable(html, lines, i);
                continue;
            }

            if (TryAppendHeading(html, line))
            {
                i++;
                continue;
            }

            if (IsListItem(line))
            {
                i = AppendList(html, lines, i);
                continue;
            }

            i = AppendParagraph(html, lines, i);
        }

        return html.ToString();
    }

    private static bool IsTableStart(string[] lines, int index)
    {
        return index + 1 < lines.Length
            && lines[index].Contains('|', StringComparison.Ordinal)
            && Regex.IsMatch(lines[index + 1], @"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$");
    }

    private static int AppendTable(StringBuilder html, string[] lines, int index)
    {
        var headers = SplitTableRow(lines[index]);
        html.Append("<div class=\"md-table-wrap\"><table class=\"table table-sm table-bordered md-table\"><thead><tr>");
        foreach (var header in headers)
        {
            html.Append("<th>").Append(RenderInline(header)).Append("</th>");
        }

        html.Append("</tr></thead><tbody>");
        index += 2;
        while (index < lines.Length && lines[index].Contains('|', StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(lines[index]))
        {
            html.Append("<tr>");
            foreach (var cell in SplitTableRow(lines[index]))
            {
                html.Append("<td>").Append(RenderInline(cell)).Append("</td>");
            }

            html.Append("</tr>");
            index++;
        }

        html.Append("</tbody></table></div>");
        return index;
    }

    private static IReadOnlyList<string> SplitTableRow(string line)
    {
        return line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToList();
    }

    private static bool TryAppendHeading(StringBuilder html, string line)
    {
        var trimmed = line.TrimStart();
        var level = trimmed.TakeWhile(c => c == '#').Count();
        if (level is < 1 or > 4 || trimmed.Length <= level || trimmed[level] != ' ')
        {
            return false;
        }

        html.Append("<h").Append(level).Append('>')
            .Append(RenderInline(trimmed[(level + 1)..]))
            .Append("</h").Append(level).Append('>');
        return true;
    }

    private static bool IsListItem(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal);
    }

    private static int AppendList(StringBuilder html, string[] lines, int index)
    {
        html.Append("<ul>");
        while (index < lines.Length && IsListItem(lines[index]))
        {
            html.Append("<li>").Append(RenderInline(lines[index].TrimStart()[2..])).Append("</li>");
            index++;
        }

        html.Append("</ul>");
        return index;
    }

    private static int AppendParagraph(StringBuilder html, string[] lines, int index)
    {
        var paragraph = new List<string>();
        while (index < lines.Length
            && !string.IsNullOrWhiteSpace(lines[index])
            && !IsTableStart(lines, index)
            && !IsListItem(lines[index])
            && !lines[index].TrimStart().StartsWith("# ", StringComparison.Ordinal)
            && !lines[index].TrimStart().StartsWith("## ", StringComparison.Ordinal))
        {
            paragraph.Add(lines[index].Trim());
            index++;
        }

        html.Append("<p>").Append(RenderInline(string.Join(" ", paragraph))).Append("</p>");
        return index;
    }

    private static string RenderInline(string value)
    {
        var encoded = HtmlEncoder.Default.Encode(value);
        encoded = Regex.Replace(encoded, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        encoded = Regex.Replace(encoded, @"`(.+?)`", "<code>$1</code>");
        return encoded;
    }
}
