using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IGraphContextBuilder
{
    Task<Result<string>> BuildContextAsync(
        string query,
        GraphContextSources sources,
        CancellationToken cancellationToken = default);
}

[Flags]
public enum GraphContextSources
{
    None = 0,
    Documents = 1,
    Entities = 2,
    Relationships = 4,
    Taxonomies = 8,
    Ontologies = 16,
    Communities = 32,
    Summaries = 64,
    All = Documents | Entities | Relationships | Taxonomies | Ontologies | Communities | Summaries
}
