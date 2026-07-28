using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aletheia.RAGS.Infrastructure.PostgreSQL.Wiki;

public sealed class PostgreSqlWikiSchemaInitializer : IHostedService
{
    private readonly PostgreSqlWikiSchema _schema;
    private readonly ILogger<PostgreSqlWikiSchemaInitializer> _logger;

    public PostgreSqlWikiSchemaInitializer(
        PostgreSqlWikiSchema schema,
        ILogger<PostgreSqlWikiSchemaInitializer> logger)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _schema.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("WRAGS wiki PostgreSQL schema is ready.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
