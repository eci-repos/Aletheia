using System.IO.Compression;
using System.Text;
using Aletheia.Repository.API.Services;

namespace Repository.UnitTests;

public sealed class UploadedFileTextExtractorTests
{
    [Fact]
    public async Task ExtractAsync_extracts_docx_body_text()
    {
        var extractor = new UploadedFileTextExtractor();
        await using var document = CreateDocx("Contract review", "Delivery milestones");

        var result = await extractor.ExtractAsync(
            "rfp-analysis.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            document);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsSupported);
        Assert.Contains("Contract review", result.Value.Text);
        Assert.Contains("Delivery milestones", result.Value.Text);
        Assert.Equal("DocxExtracted", result.Value.Status);
    }

    [Fact]
    public async Task ExtractAsync_extracts_text_files()
    {
        var extractor = new UploadedFileTextExtractor();
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("alpha\n\nbeta"));

        var result = await extractor.ExtractAsync("notes.txt", "text/plain", content);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsSupported);
        Assert.Equal($"alpha{Environment.NewLine}beta", result.Value.Text);
        Assert.Equal("TextExtracted", result.Value.Status);
    }

    [Fact]
    public async Task ExtractAsync_skips_unsupported_binary_files()
    {
        var extractor = new UploadedFileTextExtractor();
        await using var content = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await extractor.ExtractAsync("image.png", "image/png", content);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsSupported);
        Assert.Null(result.Value.Text);
        Assert.Equal("UnsupportedType", result.Value.Status);
    }

    private static MemoryStream CreateDocx(params string[] paragraphs)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream, Encoding.UTF8);
            writer.Write("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>""");
            foreach (var paragraph in paragraphs)
            {
                writer.Write($"""<w:p><w:r><w:t>{paragraph}</w:t></w:r></w:p>""");
            }
            writer.Write("</w:body></w:document>");
        }

        stream.Position = 0;
        return stream;
    }
}
