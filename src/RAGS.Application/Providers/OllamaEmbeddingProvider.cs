using System.Net.Http.Json;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aletheia.RAGS.Application.Providers;

/// <summary>Embeddings via an Ollama-compatible /api/embed endpoint (e.g., nomic-embed-text).</summary>
public sealed class OllamaEmbeddingProvider : IEmbeddingProvider, IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<OllamaEmbeddingProvider> _logger;
    private int _dimension;

    public OllamaEmbeddingProvider(
        HttpClient httpClient,
        string model,
        int dimension = 768,
        ILogger<OllamaEmbeddingProvider>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _model = string.IsNullOrWhiteSpace(model) ? throw new ArgumentException("Embedding model is required.", nameof(model)) : model;
        _dimension = dimension > 0 ? dimension : throw new ArgumentOutOfRangeException(nameof(dimension));
        _logger = logger ?? NullLogger<OllamaEmbeddingProvider>.Instance;
    }

    /// <summary>Expected dimension (configured); updated to the actual model dimension after the first successful call.</summary>
    public int VectorDimension => _dimension;

    public async Task<Result<ReadOnlyMemory<float>>> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text is required.", nameof(text));
        }

        try
        {
            var payload = new { model = _model, input = text };
            using var response = await _httpClient
                .PostAsJsonAsync("/api/embed", payload, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Result<ReadOnlyMemory<float>>.Failure(
                    $"Ollama embedding failed. HTTP {(int)response.StatusCode} {response.ReasonPhrase} for model '{_model}'.");
            }

            var result = await response.Content
                .ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken)
                .ConfigureAwait(false);

            var embedding = result?.Embeddings?.FirstOrDefault();
            if (embedding is null || embedding.Length == 0)
            {
                return Result<ReadOnlyMemory<float>>.Failure($"Ollama embedding returned no vector for model '{_model}'.");
            }

            if (_dimension != embedding.Length)
            {
                _logger.LogWarning(
                    "Ollama embedding dimension for model '{Model}' is {Actual}; configured dimension is {Configured}. Update AI:EmbeddingDimension to match.",
                    _model,
                    embedding.Length,
                    _dimension);
                _dimension = embedding.Length;
            }

            return Result<ReadOnlyMemory<float>>.Success(new ReadOnlyMemory<float>(embedding));
        }
        catch (Exception ex)
        {
            return Result<ReadOnlyMemory<float>>.Failure($"Ollama embedding failed for model '{_model}'. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed class OllamaEmbedResponse
    {
        public List<float[]>? Embeddings { get; set; }
    }
}
