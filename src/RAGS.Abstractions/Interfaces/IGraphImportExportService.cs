using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IGraphImportExportService
{
    Task<Result> ImportAsync(Stream data, string format, CancellationToken cancellationToken = default);

    Task<Result<Stream>> ExportAsync(string format, CancellationToken cancellationToken = default);

    Task<Result<Stream>> ExportSubgraphAsync(string nodeId, int depth, string format, CancellationToken cancellationToken = default);

    Task<Result> BackupAsync(string path, CancellationToken cancellationToken = default);

    Task<Result> RestoreAsync(string path, CancellationToken cancellationToken = default);
}
