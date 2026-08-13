namespace Aletheia.RAGS.Abstractions.Models;

/// <summary>
/// A page boundary in extracted text: the 1-based page number and the character range the page
/// occupies in the normalized extraction text. Produced by page-aware extractors (PDF) and consumed
/// by <c>ChunkingPipeline</c> to stamp each chunk with its source page. Offsets are into the
/// normalized text exactly as returned by the extractor.
/// </summary>
public sealed record TextPage(int PageNumber, int StartOffset, int Length)
{
    public int EndOffset => StartOffset + Length;
}
