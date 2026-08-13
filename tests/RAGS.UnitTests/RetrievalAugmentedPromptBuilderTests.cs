using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.SemanticKernel;

namespace RAGS.UnitTests;

public class RetrievalAugmentedPromptBuilderTests
{
    [Fact]
    public void BuildCitations_maps_sequential_numbers_to_source_chunks()
    {
        var sourceA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();
        var results = new[]
        {
            new SearchResult(new Chunk(Guid.NewGuid(), sourceA, "first chunk from A", 0, pageNumber: 2), 0.9f, Array.Empty<string>(), retrievalStrategy: "semantic", rank: 1),
            new SearchResult(new Chunk(Guid.NewGuid(), sourceB, "first chunk from B", 0, pageNumber: 5), 0.8f, Array.Empty<string>(), retrievalStrategy: "semantic", rank: 2),
            new SearchResult(new Chunk(Guid.NewGuid(), sourceA, "second chunk from A", 1, pageNumber: 3), 0.7f, Array.Empty<string>(), retrievalStrategy: "semantic", rank: 3)
        };

        var citations = RetrievalAugmentedPromptBuilder.BuildCitations(results);

        // Citation numbers are assigned grouped by source (matching the prompt's source blocks):
        // source A's chunks get [1] and [2], source B's chunk gets [3].
        Assert.Equal(3, citations.Count);
        Assert.Equal(1, citations[0].Number);
        Assert.Equal(sourceA, citations[0].SourceId);
        Assert.Equal(2, citations[0].PageNumber);
        Assert.Equal(2, citations[1].Number);
        Assert.Equal(sourceA, citations[1].SourceId);
        Assert.Equal(3, citations[1].PageNumber);
        Assert.Equal(3, citations[2].Number);
        Assert.Equal(sourceB, citations[2].SourceId);
        Assert.Equal(5, citations[2].PageNumber);
    }

    [Fact]
    public void BuildCitations_uses_leading_phrase_of_chunk_content()
    {
        var sourceId = Guid.NewGuid();
        var longContent = string.Concat(Enumerable.Repeat("word ", 40));
        var results = new[]
        {
            new SearchResult(new Chunk(Guid.NewGuid(), sourceId, longContent, 0), 0.9f, Array.Empty<string>(), retrievalStrategy: "semantic", rank: 1)
        };

        var citations = RetrievalAugmentedPromptBuilder.BuildCitations(results);

        Assert.Single(citations);
        Assert.Equal(100, citations[0].LeadingPhrase.Length);
        Assert.StartsWith("word", citations[0].LeadingPhrase, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCitations_returns_empty_for_null_or_empty_results()
    {
        Assert.Empty(RetrievalAugmentedPromptBuilder.BuildCitations(null!));
        Assert.Empty(RetrievalAugmentedPromptBuilder.BuildCitations(Array.Empty<SearchResult>()));
    }
}
