using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;

namespace Aletheia.RAGS.Application.Providers;

public sealed class SimpleEmbeddingProvider : IEmbeddingProvider, IEmbeddingService
{
    public int VectorDimension => 128;

    public Task<Result<ReadOnlyMemory<float>>> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text is required.", nameof(text));
        }

        var vector = new float[VectorDimension];
        var normalized = text.ToLowerInvariant();

        // Deterministic frequency-based embedding
        for (var i = 0; i < normalized.Length; i++)
        {
            var index = normalized[i] % VectorDimension;
            vector[index] += 1.0f;
        }

        // Add bigram contribution for more structure
        for (var i = 0; i < normalized.Length - 1; i++)
        {
            var index = (normalized[i] + normalized[i + 1]) % VectorDimension;
            vector[index] += 0.5f;
        }

        // Normalize
        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return Task.FromResult(Result<ReadOnlyMemory<float>>.Success(vector));
    }
}
