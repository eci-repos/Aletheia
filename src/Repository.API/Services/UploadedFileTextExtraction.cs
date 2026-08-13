using System.IO.Compression;
using System.Text;
using System.Xml;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;
using UglyToad.PdfPig;

namespace Aletheia.Repository.API.Services;

public interface IUploadedFileTextExtractor
{
    Task<Result<UploadedFileTextExtraction>> ExtractAsync(
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default);
}

public sealed record UploadedFileTextExtraction(
    bool IsSupported,
    string? Text,
    string Status,
    IReadOnlyList<TextPage>? Pages = null);

public sealed class UploadedFileTextExtractor : IUploadedFileTextExtractor
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".md",
        ".markdown",
        ".csv",
        ".json",
        ".xml",
        ".yaml",
        ".yml",
        ".log"
    };

    public async Task<Result<UploadedFileTextExtraction>> ExtractAsync(
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Result<UploadedFileTextExtraction>.Failure("File name is required for ingestion.");
        }

        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        if (!content.CanRead)
        {
            return Result<UploadedFileTextExtraction>.Failure("Uploaded file stream is not readable.");
        }

        ResetIfSeekable(content);

        try
        {
            if (IsPdf(fileName, contentType))
            {
                try
                {
                    var text = ExtractPdfText(content);
                    return Result<UploadedFileTextExtraction>.Success(text);
                }
                catch (Exception ex)
                {
                    return Result<UploadedFileTextExtraction>.Failure($"PDF extraction failed. {ex.Message}");
                }
            }

            if (IsDocx(fileName, contentType))
            {
                var text = ExtractDocxText(content);
                return Result<UploadedFileTextExtraction>.Success(CreateSupported(text, "DocxExtracted"));
            }

            if (IsTextLike(fileName, contentType))
            {
                using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                return Result<UploadedFileTextExtraction>.Success(CreateSupported(text, "TextExtracted"));
            }

            return Result<UploadedFileTextExtraction>.Success(new UploadedFileTextExtraction(false, null, "UnsupportedType"));
        }
        catch (InvalidDataException ex)
        {
            return Result<UploadedFileTextExtraction>.Failure($"Document extraction failed. {ex.Message}");
        }
        catch (XmlException ex)
        {
            return Result<UploadedFileTextExtraction>.Failure($"Document XML extraction failed. {ex.Message}");
        }
    }

    private static UploadedFileTextExtraction CreateSupported(string text, string status, IReadOnlyList<TextPage>? pages = null)
    {
        // Page-aware paths (PDF) build the normalized text page-by-page and pass pages; the
        // generic paths normalize here. Re-normalizing a page-aware text would break page offsets.
        var normalized = pages is null ? NormalizeWhitespace(text) : text;
        return new UploadedFileTextExtraction(true, normalized, status, pages);
    }

    public static bool IsPdf(string fileName, string contentType)
    {
        return string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static UploadedFileTextExtraction ExtractPdfText(Stream content)
    {
        using var document = PdfDocument.Open(content);
        var builder = new StringBuilder();
        var pages = new List<TextPage>();
        var offset = 0;

        foreach (var page in document.GetPages())
        {
            var normalized = NormalizeWhitespace(page.Text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                offset += Environment.NewLine.Length;
            }

            pages.Add(new TextPage(page.Number, offset, normalized.Length));
            builder.Append(normalized);
            offset += normalized.Length;
        }

        return CreateSupported(builder.ToString(), "PdfExtracted", pages);
    }

    private static bool IsDocx(string fileName, string contentType)
    {
        return string.Equals(Path.GetExtension(fileName), ".docx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextLike(string fileName, string contentType)
    {
        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/x-yaml", StringComparison.OrdinalIgnoreCase)
            || TextExtensions.Contains(Path.GetExtension(fileName));
    }

    private static string ExtractDocxText(Stream content)
    {
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        var documentEntry = archive.GetEntry("word/document.xml");
        if (documentEntry is null)
        {
            throw new InvalidDataException("The DOCX package does not contain word/document.xml.");
        }

        var builder = new StringBuilder();
        AppendWordXmlText(documentEntry, builder);

        foreach (var entry in archive.Entries
            .Where(e => IsWordHeaderOrFooter(e.FullName))
            .OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            builder.AppendLine();
            AppendWordXmlText(entry, builder);
        }

        return builder.ToString();
    }

    private static void AppendWordXmlText(ZipArchiveEntry entry, StringBuilder builder)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        });

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "t":
                    builder.Append(reader.ReadElementContentAsString());
                    break;
                case "tab":
                    builder.Append('\t');
                    break;
                case "br":
                case "cr":
                case "p":
                    builder.AppendLine();
                    break;
            }
        }
    }

    private static bool IsWordHeaderOrFooter(string fullName)
    {
        return fullName.StartsWith("word/header", StringComparison.OrdinalIgnoreCase)
            || fullName.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeWhitespace(string text)
    {
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));

        return string.Join(Environment.NewLine, lines);
    }

    private static void ResetIfSeekable(Stream content)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
        }
    }
}
