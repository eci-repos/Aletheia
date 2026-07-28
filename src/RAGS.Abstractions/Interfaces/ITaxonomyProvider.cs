using Aletheia.Foundation.Shared;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface ITaxonomyProvider
{
    Task<Result<IReadOnlyCollection<string>>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyCollection<string>>> GetTagsAsync(string category, CancellationToken cancellationToken = default);
}
