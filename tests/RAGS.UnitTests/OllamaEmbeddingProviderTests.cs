using System.Net;
using System.Text;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Application.Providers;

namespace RAGS.UnitTests;

public class OllamaEmbeddingProviderTests
{
    [Fact]
    public async Task GenerateAsync_returns_vector_from_embed_endpoint()
    {
        var handler = new FakeEmbeddingHandler(HttpStatusCode.OK, "{\"embeddings\": [[0.1, 0.2, 0.3]]}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaEmbeddingProvider(client, "nomic-embed-text");

        var result = await provider.GenerateAsync("hello");

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Length);
        Assert.Equal(3, provider.VectorDimension);
        Assert.Equal("/api/embed", handler.LastPath);
        Assert.Contains("nomic-embed-text", handler.LastBody);
    }

    [Fact]
    public async Task GenerateAsync_returns_failure_on_http_error()
    {
        var handler = new FakeEmbeddingHandler(HttpStatusCode.InternalServerError, "boom");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaEmbeddingProvider(client, "nomic-embed-text");

        var result = await provider.GenerateAsync("hello");

        Assert.True(result.IsFailure);
        Assert.Contains("500", result.Error);
    }

    [Fact]
    public async Task GenerateAsync_returns_failure_when_no_embeddings()
    {
        var handler = new FakeEmbeddingHandler(HttpStatusCode.OK, "{\"embeddings\": []}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaEmbeddingProvider(client, "nomic-embed-text");

        var result = await provider.GenerateAsync("hello");

        Assert.True(result.IsFailure);
        Assert.Contains("no vector", result.Error);
    }

    [Fact]
    public async Task GenerateAsync_returns_failure_when_http_throws()
    {
        var handler = new ThrowingEmbeddingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var provider = new OllamaEmbeddingProvider(client, "nomic-embed-text");

        var result = await provider.GenerateAsync("hello");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Constructor_rejects_empty_model_or_dimension()
    {
        using var client = new HttpClient();
        Assert.Throws<ArgumentException>(() => new OllamaEmbeddingProvider(client, "  "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OllamaEmbeddingProvider(client, "model", 0));
    }

    private sealed class FakeEmbeddingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public FakeEmbeddingHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        public string? LastPath { get; private set; }

        public string? LastBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri?.AbsolutePath;
            LastBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingEmbeddingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("connection refused");
        }
    }
}
