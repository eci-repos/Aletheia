using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

public interface IWikiPageRepository
{
    Task<Result<IReadOnlyList<WikiPage>>> SearchAsync(
        string query,
        int topK,
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

    Task<Result<IReadOnlyList<WikiPage>>> UpsertAsync(
        IReadOnlyList<WikiPage> pages,
        CancellationToken cancellationToken = default);

    Task<Result<WikiPage?>> UpdateStatusAsync(
        Guid pageId,
        string status,
        string? reviewedBy,
        CancellationToken cancellationToken = default);

    Task<Result<WikiPage?>> UpdatePageAsync(
        Guid pageId,
        WikiPageEditRequest request,
        CancellationToken cancellationToken = default);
}
