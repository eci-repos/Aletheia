using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IWragsWikiService
{
    Task<Result<IReadOnlyList<WikiPage>>> SearchAsync(
        WikiSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<WikiPage>>> RegenerateAsync(
        WikiSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<WikiPage>>> GetRecentAsync(
        int take,
        CancellationToken cancellationToken = default);

    Task<Result<WikiPage?>> GetAsync(
        Guid pageId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<WikiPageLink>>> GetRelatedAsync(
        Guid pageId,
        int take,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<WikiPageHistoryEntry>>> GetHistoryAsync(
        Guid pageId,
        int take,
        CancellationToken cancellationToken = default);

    Task<Result<WikiPage?>> UpdateStatusAsync(
        Guid pageId,
        WikiPageStatusUpdate update,
        CancellationToken cancellationToken = default);

    Task<Result<WikiPage?>> UpdatePageAsync(
        Guid pageId,
        WikiPageEditRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SearchResult>>> RetrieveAsync(
        WikiSearchRequest request,
        CancellationToken cancellationToken = default);
}
