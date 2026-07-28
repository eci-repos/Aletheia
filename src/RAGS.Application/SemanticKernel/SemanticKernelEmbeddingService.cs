using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;

namespace Aletheia.RAGS.Application.SemanticKernel;

public sealed class SemanticKernelEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingProvider _fallbackProvider;

    public SemanticKernelEmbeddingService(IEmbeddingProvider fallbackProvider)
    {
        _fallbackProvider = fallbackProvider ?? throw new ArgumentNullException(nameof(fallbackProvider));
    }

    public int VectorDimension => _fallbackProvider.VectorDimension;

    public Task<Result<ReadOnlyMemory<float>>> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        return _fallbackProvider.GenerateAsync(text, cancellationToken);
    }
}
