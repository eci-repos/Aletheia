using System.Net;
using System.Text;
using System.Text.Json;
using Aletheia.Web.Services;
using Microsoft.JSInterop;
using Xunit;

namespace Aletheia.Web.UnitTests;

public class RepositoryApiClientUploadTests
{
    [Fact]
    public async Task UploadAsync_maps_conflict_to_duplicate_result()
    {
        var body = JsonSerializer.Serialize(new
        {
            duplicate = true,
            noChange = false,
            message = "This exact file is already in the repository (uploaded 8/5/2026 as 'report.pdf'). Nothing was uploaded.",
            existingFileId = Guid.NewGuid(),
            existingFileName = "report.pdf",
            existingUploadedAt = DateTimeOffset.UtcNow,
            existingVersion = (string?)null
        });

        using var client = CreateClient(HttpStatusCode.Conflict, body);
        var api = CreateApiClient(client);

        var result = await api.UploadAsync(Guid.NewGuid(), "report.pdf", "application/pdf", new MemoryStream(new byte[] { 1 }), 1);

        Assert.False(result.Uploaded);
        Assert.True(result.IsDuplicate);
        Assert.False(result.NoChange);
        Assert.Equal("Duplicate", result.IngestionStatus);
        Assert.Contains("already in the repository", result.DuplicateMessage);
        Assert.NotNull(result.ExistingFileId);
        Assert.Equal("report.pdf", result.ExistingFileName);
    }

    [Fact]
    public async Task UploadAsync_maps_conflict_to_no_change_result()
    {
        var fileId = Guid.NewGuid();
        var body = JsonSerializer.Serialize(new
        {
            duplicate = true,
            noChange = true,
            message = "'report.pdf' is already up to date with this exact content. No new version was created.",
            existingFileId = fileId,
            existingFileName = "report.pdf",
            existingUploadedAt = DateTimeOffset.UtcNow,
            existingVersion = (string?)null
        });

        using var client = CreateClient(HttpStatusCode.Conflict, body);
        var api = CreateApiClient(client);

        var result = await api.UploadAsync(Guid.NewGuid(), "report.pdf", "application/pdf", new MemoryStream(new byte[] { 1 }), 1, existingFileId: fileId);

        Assert.False(result.Uploaded);
        Assert.True(result.IsDuplicate);
        Assert.True(result.NoChange);
        Assert.Equal("NoChange", result.IngestionStatus);
        Assert.Contains("up to date", result.DuplicateMessage);
    }

    [Fact]
    public async Task UploadAsync_maps_success_with_ingestion_job()
    {
        var jobId = Guid.NewGuid();
        var body = JsonSerializer.Serialize(new
        {
            Metadata = (object?)null,
            RagsIngested = false,
            KnowledgeIndexed = false,
            IngestionStatus = "Queued",
            IngestionError = (string?)null,
            IngestionJobId = jobId
        });

        using var client = CreateClient(HttpStatusCode.OK, body);
        var api = CreateApiClient(client);

        var result = await api.UploadAsync(Guid.NewGuid(), "report.pdf", "application/pdf", new MemoryStream(new byte[] { 1 }), 1);

        Assert.True(result.Uploaded);
        Assert.False(result.IsDuplicate);
        Assert.Equal("Queued", result.IngestionStatus);
        Assert.Equal(jobId, result.IngestionJobId);
    }

    [Fact]
    public async Task UploadAsync_reports_failure_on_server_error()
    {
        var body = "{\"error\":\"storage unavailable\"}";

        using var client = CreateClient(HttpStatusCode.BadRequest, body);
        var api = CreateApiClient(client);

        var result = await api.UploadAsync(Guid.NewGuid(), "report.pdf", "application/pdf", new MemoryStream(new byte[] { 1 }), 1);

        Assert.False(result.Uploaded);
        Assert.False(result.IsDuplicate);
        Assert.Equal("UploadFailed", result.IngestionStatus);
        Assert.Contains("storage unavailable", result.Error);
    }

    private static RepositoryApiClient CreateApiClient(HttpClient httpClient)
    {
        var factory = new FakeHttpClientFactory(httpClient);
        var jsRuntime = new FakeJSRuntime();
        return new RepositoryApiClient(factory, new AuthService(factory, jsRuntime));
    }

    private static HttpClient CreateClient(HttpStatusCode statusCode, string body)
    {
        var handler = new FakeHttpMessageHandler(statusCode, body);
        // Production wires BaseAddress on the "RepositoryApi" client (Program.cs ConfigureRepositoryApi);
        // the client uses relative request URIs, so the fake must mirror that or HttpMessageInvoker throws.
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public FakeHttpMessageHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _httpClient;

        public FakeHttpClientFactory(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public HttpClient CreateClient(string name) => _httpClient;
    }

    private sealed class FakeJSRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => new(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => new(default(TValue)!);
    }
}
