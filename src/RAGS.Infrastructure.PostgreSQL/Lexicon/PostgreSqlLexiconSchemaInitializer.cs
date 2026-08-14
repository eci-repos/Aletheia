using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aletheia.RAGS.Infrastructure.PostgreSQL.Lexicon;

public sealed class PostgreSqlLexiconSchemaInitializer : IHostedService
{
    private readonly PostgreSqlLexiconSchema _schema;
    private readonly ILogger<PostgreSqlLexiconSchemaInitializer> _logger;

    public PostgreSqlLexiconSchemaInitializer(
        PostgreSqlLexiconSchema schema,
        ILogger<PostgreSqlLexiconSchemaInitializer> logger)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _schema.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Normalized lexicon PostgreSQL schema is ready.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
