using Aletheia.Repository.Abstractions.Models;
using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Aletheia.Repository.Infrastructure.PostgreSQL.Metadata;
using Repository.IntegrationTests.Fixtures;

namespace Repository.IntegrationTests;

public class PostgreSqlTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MetadataRepository_can_save_and_retrieve()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var factory = new PostgreSqlConnectionFactory(_fixture.ConnectionString);
        var repository = new PostgreSqlMetadataRepository(factory);

        var descriptor = new FileDescriptor(Guid.NewGuid(), "test.txt");
        var metadata = new FileMetadata(descriptor, "text/plain", 12, DateTimeOffset.UtcNow);

        var saveResult = await repository.SaveAsync(metadata);
        Assert.True(saveResult.IsSuccess);

        var getResult = await repository.GetAsync(descriptor);
        Assert.True(getResult.IsSuccess);
        Assert.Equal("test.txt", getResult.Value!.Descriptor.FileName);
    }

    [Fact]
    public async Task MetadataRepository_search_returns_paged_results()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var factory = new PostgreSqlConnectionFactory(_fixture.ConnectionString);
        var repository = new PostgreSqlMetadataRepository(factory);

        var descriptor = new FileDescriptor(Guid.NewGuid(), "searchable.txt");
        var metadata = new FileMetadata(descriptor, "text/plain", 12, DateTimeOffset.UtcNow);
        await repository.SaveAsync(metadata);

        var request = new SearchRequest("searchable", 1, 10);
        var result = await repository.SearchAsync(request);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.TotalCount >= 1);
    }
}
