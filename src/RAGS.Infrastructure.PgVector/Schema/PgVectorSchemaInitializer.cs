using Aletheia.Repository.Infrastructure.PostgreSQL.Connections;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aletheia.RAGS.Infrastructure.PgVector.Schema;

public sealed class PgVectorSchemaInitializer : BackgroundService
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly int _vectorDimension;
    private readonly string _indexType;
    private readonly ILogger<PgVectorSchemaInitializer> _logger;

    public PgVectorSchemaInitializer(
        PostgreSqlConnectionFactory connectionFactory,
        int vectorDimension,
        string indexType,
        ILogger<PgVectorSchemaInitializer> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _vectorDimension = vectorDimension > 0 ? vectorDimension : throw new ArgumentOutOfRangeException(nameof(vectorDimension));
        _indexType = string.IsNullOrWhiteSpace(indexType) ? "hnsw" : indexType;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var schema = new PgVectorSchema(_connectionFactory, _vectorDimension, _indexType);
            await schema.EnsureCreatedAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("PgVector schema initialized with dimension {Dimension} and index type {IndexType}.", _vectorDimension, _indexType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize PgVector schema.");
            throw;
        }
    }
}
