using System.Text;
using System.Text.RegularExpressions;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Application;

/// <summary>
/// Loads document templates from <c>docs/doc-templates</c>. Each template enumerates the ordered
/// sections (headings with a short description) that every document of that kind is expected to cover.
/// </summary>
public sealed class DocumentTemplateRegistry : IDocumentTemplateRegistry
{
    private const int MaxDescriptionLength = 250;

    private readonly IReadOnlyDictionary<string, IReadOnlyList<DocumentTemplateSection>> _templates;

    public DocumentTemplateRegistry()
    {
        _templates = LoadTemplates();
    }

    public IReadOnlyList<DocumentTemplateSection>? TryGetSections(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var canonicalName = FindTemplateName(fileName);
        return canonicalName is null ? null : _templates[canonicalName];
    }

    public string? TryGetCanonicalName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return FindTemplateName(fileName);
    }

    private string? FindTemplateName(string fileName)
    {
        var fileTokens = Tokenize(fileName);
        foreach (var template in _templates)
        {
            if (MatchesTemplate(template.Key, fileTokens))
            {
                return template.Key;
            }
        }

        return null;
    }

    private static bool MatchesTemplate(string templateName, IReadOnlySet<string> fileTokens)
    {
        var templateTokens = Tokenize(templateName);
        var overlap = templateTokens.Count(token => fileTokens.Contains(token));
        return overlap >= 2 || (templateTokens.Count >= 1 && templateTokens.Count <= 1 && overlap >= 1);
    }

    private static HashSet<string> Tokenize(string value)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in System.Text.RegularExpressions.Regex.Matches(value, "[A-Za-z0-9]+"))
        {
            var token = match.Value;
            if (token.Length >= 3 || token.All(char.IsDigit))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    private static Dictionary<string, IReadOnlyList<DocumentTemplateSection>> LoadTemplates()
    {
        var templates = new Dictionary<string, IReadOnlyList<DocumentTemplateSection>>(StringComparer.OrdinalIgnoreCase);
        var folder = LocateDocTemplatesFolder();
        if (folder is null)
        {
            return templates;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.md"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var sections = ParseSections(file);
            if (sections.Count > 0)
            {
                templates[name] = sections;
            }
        }

        return templates;
    }

    private static readonly Regex SectionPattern = new(@"^\d+\.\s+\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex HeadingPattern = new(@"^##\s+(.+)$", RegexOptions.Compiled);

    private static IReadOnlyList<DocumentTemplateSection> ParseSections(string file)
    {
        var lines = File.ReadAllLines(file);
        var hasNumberedSections = lines.Any(line => SectionPattern.IsMatch(line.Trim()));
        return hasNumberedSections
            ? ParseNumberedSections(lines)
            : ParseHeadingSections(lines);
    }

    private static IReadOnlyList<DocumentTemplateSection> ParseNumberedSections(IReadOnlyList<string> lines)
    {
        var sections = new List<DocumentTemplateSection>();
        string? currentTitle = null;
        var description = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            var match = SectionPattern.Match(line);
            if (match.Success)
            {
                if (currentTitle is not null)
                {
                    sections.Add(new DocumentTemplateSection(currentTitle, description.ToString().Trim()));
                }

                currentTitle = match.Groups[1].Value.Trim();
                description.Clear();
            }
            else if (currentTitle is not null && description.Length == 0
                && !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
            {
                description.Append(line.Length > MaxDescriptionLength ? line[..MaxDescriptionLength] : line);
            }
        }

        if (currentTitle is not null)
        {
            sections.Add(new DocumentTemplateSection(currentTitle, description.ToString().Trim()));
        }

        return sections;
    }

    private static IReadOnlyList<DocumentTemplateSection> ParseHeadingSections(IReadOnlyList<string> lines)
    {
        var sections = new List<DocumentTemplateSection>();
        string? currentTitle = null;
        var description = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            var match = HeadingPattern.Match(line);
            if (match.Success)
            {
                if (currentTitle is not null)
                {
                    sections.Add(new DocumentTemplateSection(currentTitle, description.ToString().Trim()));
                }

                currentTitle = match.Groups[1].Value.Trim();
                description.Clear();
            }
            else if (currentTitle is not null && description.Length == 0
                && !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
            {
                description.Append(line.Length > MaxDescriptionLength ? line[..MaxDescriptionLength] : line);
            }
        }

        if (currentTitle is not null)
        {
            sections.Add(new DocumentTemplateSection(currentTitle, description.ToString().Trim()));
        }

        return sections;
    }

    private static string? LocateDocTemplatesFolder()
    {
        var candidate = Path.GetFullPath(AppContext.BaseDirectory);
        for (var depth = 0; depth < 12; depth++)
        {
            var folder = Path.Combine(candidate, "docs", "doc-templates");
            if (Directory.Exists(folder))
            {
                return Path.GetFullPath(folder);
            }

            var parent = Path.GetDirectoryName(candidate);
            if (string.IsNullOrEmpty(parent) || parent == candidate)
            {
                break;
            }

            candidate = parent;
        }

        return null;
    }
}
