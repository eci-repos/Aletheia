using Aletheia.RAGS.Application.Providers;

namespace RAGS.UnitTests;

public class SimpleEmbeddingProviderTests
{
    [Fact]
    public void VectorDimension_is_128()
    {
        var provider = new SimpleEmbeddingProvider();

        Assert.Equal(128, provider.VectorDimension);
    }

    [Fact]
    public async Task GenerateAsync_returns_normalized_vector()
    {
        var provider = new SimpleEmbeddingProvider();

        var result = await provider.GenerateAsync("hello world");

        Assert.True(result.IsSuccess);
        Assert.Equal(128, result.Value.Length);

        // Verify normalization: L2 norm should be ~1
        var norm = MathF.Sqrt(Enumerable.Range(0, 128).Sum(i => result.Value.Span[i] * result.Value.Span[i]));
        Assert.InRange(norm, 0.99f, 1.01f);
    }

    [Fact]
    public async Task GenerateAsync_returns_different_vectors_for_different_texts()
    {
        var provider = new SimpleEmbeddingProvider();

        var result1 = await provider.GenerateAsync("hello");
        var result2 = await provider.GenerateAsync("world");

        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);

        // Vectors should be different
        var allSame = true;
        for (var i = 0; i < 128; i++)
        {
            if (Math.Abs(result1.Value.Span[i] - result2.Value.Span[i]) > 0.001f)
            {
                allSame = false;
                break;
            }
        }

        Assert.False(allSame);
    }

    [Fact]
    public async Task GenerateAsync_throws_when_text_is_empty()
    {
        var provider = new SimpleEmbeddingProvider();

        await Assert.ThrowsAsync<ArgumentException>(() => provider.GenerateAsync(""));
    }
}
