using System.IO.Compression;
using System.Text;
using System.Xml;
using Aletheia.Foundation.Shared;

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
    string Status);

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

    private static UploadedFileTextExtraction CreateSupported(string text, string status)
    {
        var normalized = NormalizeWhitespace(text);
        return new UploadedFileTextExtraction(true, normalized, status);
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
