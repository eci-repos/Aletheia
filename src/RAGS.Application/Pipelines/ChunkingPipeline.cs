using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Application.Pipelines;

public sealed class ChunkingPipeline
{
    private const int DefaultChunkSize = 1000;
    private const int DefaultOverlap = 200;

    public IReadOnlyList<Chunk> Chunk(Guid sourceId, string content, int? chunkSize = null, int? overlap = null)
    {
        return Chunk(sourceId, content, pages: null, chunkSize, overlap);
    }

    public IReadOnlyList<Chunk> Chunk(
        Guid sourceId,
        string content,
        IReadOnlyList<TextPage>? pages,
        int? chunkSize = null,
        int? overlap = null)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Source ID is required.", nameof(sourceId));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content is required.", nameof(content));
        }

        var size = chunkSize ?? DefaultChunkSize;
        var over = overlap ?? DefaultOverlap;

        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than zero.");
        }

        if (over < 0 || over >= size)
        {
            throw new ArgumentOutOfRangeException(nameof(overlap), "Overlap must be non-negative and less than chunk size.");
        }

        var chunks = new List<Chunk>();
        var index = 0;
        var position = 0;

        while (position < content.Length)
        {
            var length = Math.Min(size, content.Length - position);
            var chunkContent = content.Substring(position, length);
            var (pageNumber, offsetInPage) = ResolvePage(pages, position);
            chunks.Add(new Chunk(Guid.NewGuid(), sourceId, chunkContent, index++, pageNumber, offsetInPage));
            position += size - over;
        }

        return chunks;
    }

    private static (int? PageNumber, int? OffsetInPage) ResolvePage(IReadOnlyList<TextPage>? pages, int position)
    {
        if (pages is null || pages.Count == 0)
        {
            return (null, null);
        }

        // The page whose range contains the chunk start; fall back to the last page that starts
        // at or before the position (a chunk may straddle a page boundary — it is stamped with
        // the page it starts on).
        TextPage? page = null;
        foreach (var candidate in pages)
        {
            if (candidate.StartOffset > position)
            {
                break;
            }

            page = candidate;
        }

        return page is null
            ? (null, null)
            : (page.PageNumber, position - page.StartOffset);
    }
}
