using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.Configuration;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace RAGS.UnitTests;

public sealed class SemanticKernelPluginRegistrationTests
{
    [Fact]
    public void AddAletheiaAI_registers_agentic_knowledge_plugins_on_kernel()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRagsService, EmptyRagsService>();
        services.AddSingleton<IGraphRagService, EmptyGraphRagService>();
        services.AddSingleton<ILazyGraphRagService, EmptyLazyGraphRagService>();
        services.AddSingleton<IGlobalGraphSearchService, EmptyGlobalGraphSearchService>();
        services.AddSingleton<IMetadataRepository, EmptyMetadataRepository>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:DefaultProvider"] = "None"
            })
            .Build();

        services.AddAletheiaAI(configuration);
        using var provider = services.BuildServiceProvider();

        var kernel = provider.GetRequiredService<Kernel>();
        var functionNames = kernel.Plugins
            .GetFunctionsMetadata()
            .Select(metadata => $"{metadata.PluginName}.{metadata.Name}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("AletheiaKnowledgePlugin.SearchRags", functionNames);
        Assert.Contains("AletheiaKnowledgePlugin.SearchGraphRag", functionNames);
        Assert.Contains("AletheiaKnowledgePlugin.SearchLazyGraphRag", functionNames);
        Assert.Contains("AletheiaKnowledgePlugin.SearchGlobalGraph", functionNames);
        Assert.Contains("AletheiaKnowledgePlugin.ResolveKnowledgeSource", functionNames);
        Assert.Contains("AletheiaKnowledgePlugin.EnsureSourceIngested", functionNames);
        Assert.Contains("RepositoryTool.SearchRepositoryDocuments", functionNames);
        Assert.Contains("RepositoryTool.SearchRepositoryGraphRag", functionNames);
        Assert.Contains("RepositoryTool.ResolveRepositorySource", functionNames);
    }

    private sealed class EmptyRagsService : IRagsService
    {
        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));
    }

    private sealed class EmptyGraphRagService : IGraphRagService
    {
        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(
            string query,
            int topK = 5,
            int maxExpanded = 10,
            CancellationToken cancellationToken = default,
            IReadOnlyList<Guid>? sourceIds = null)
            => Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));

        public Task<Result<GlobalSearchResult>> GlobalSearchAsync(string query, CancellationToken cancellationToken = default, IReadOnlyList<Guid>? sourceIds = null)
            => Task.FromResult(Result<GlobalSearchResult>.Success(new GlobalSearchResult(string.Empty, Array.Empty<string>(), Array.Empty<SearchResult>())));
    }

    private sealed class EmptyLazyGraphRagService : ILazyGraphRagService
    {
        public Task<Result> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());

        public Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(
            string query,
            int topK = 5,
            int maxExpanded = 10,
            CancellationToken cancellationToken = default,
            IReadOnlyList<Guid>? sourceIds = null)
            => Task.FromResult(Result<IReadOnlyList<SearchResult>>.Success(Array.Empty<SearchResult>()));

        public Task<Result<GlobalSearchResult>> GlobalSearchAsync(string query, CancellationToken cancellationToken = default, IReadOnlyList<Guid>? sourceIds = null)
            => Task.FromResult(Result<GlobalSearchResult>.Success(new GlobalSearchResult(string.Empty, Array.Empty<string>(), Array.Empty<SearchResult>())));
    }

    private sealed class EmptyGlobalGraphSearchService : IGlobalGraphSearchService
    {
        public Task<Result<GlobalSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default, IReadOnlyList<Guid>? sourceIds = null)
            => Task.FromResult(Result<GlobalSearchResult>.Success(new GlobalSearchResult(string.Empty, Array.Empty<string>(), Array.Empty<SearchResult>())));
    }

    private sealed class EmptyMetadataRepository : IMetadataRepository
    {
        public Task<Result<FileMetadata>> GetAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<FileMetadata>.Failure("Not used."));

        public Task<Result<FileMetadata>> SaveAsync(FileMetadata metadata, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<FileMetadata>.Success(metadata));

        public Task<Result<PagedResult<FileMetadata>>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<PagedResult<FileMetadata>>.Success(new PagedResult<FileMetadata>(Array.Empty<FileMetadata>(), 1, 10, 0)));

        public Task<Result> DeleteAsync(FileDescriptor descriptor, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());
    }
}
