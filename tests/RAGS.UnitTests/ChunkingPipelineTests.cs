using Aletheia.RAGS.Application.Pipelines;

namespace RAGS.UnitTests;

public class ChunkingPipelineTests
{
    [Fact]
    public void Chunk_throws_when_sourceId_is_empty()
    {
        var pipeline = new ChunkingPipeline();

        Assert.Throws<ArgumentException>(() => pipeline.Chunk(Guid.Empty, "content"));
    }

    [Fact]
    public void Chunk_throws_when_content_is_null_or_whitespace()
    {
        var pipeline = new ChunkingPipeline();

        Assert.Throws<ArgumentException>(() => pipeline.Chunk(Guid.NewGuid(), ""));
        Assert.Throws<ArgumentException>(() => pipeline.Chunk(Guid.NewGuid(), "   "));
    }

    [Fact]
    public void Chunk_returns_single_chunk_when_content_is_shorter_than_chunk_size()
    {
        var pipeline = new ChunkingPipeline();
        var sourceId = Guid.NewGuid();

        var chunks = pipeline.Chunk(sourceId, "short", chunkSize: 100, overlap: 0);

        Assert.Single(chunks);
        Assert.Equal("short", chunks[0].Content);
        Assert.Equal(sourceId, chunks[0].SourceId);
        Assert.Equal(0, chunks[0].Index);
    }

    [Fact]
    public void Chunk_splits_content_into_multiple_chunks()
    {
        var pipeline = new ChunkingPipeline();
        var sourceId = Guid.NewGuid();
        var content = new string('a', 250);

        var chunks = pipeline.Chunk(sourceId, content, chunkSize: 100, overlap: 0);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(100, chunks[0].Content.Length);
        Assert.Equal(100, chunks[1].Content.Length);
        Assert.Equal(50, chunks[2].Content.Length);
    }

    [Fact]
    public void Chunk_overlap_creates_overlapping_content()
    {
        var pipeline = new ChunkingPipeline();
        var sourceId = Guid.NewGuid();
        var content = new string('a', 200);

        var chunks = pipeline.Chunk(sourceId, content, chunkSize: 100, overlap: 50);

        Assert.Equal(4, chunks.Count);
        // With chunk size 100 and overlap 50: positions are 0, 50, 100, 150
    }
}
