using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Abstractions.Interfaces;

public interface ICollaborationService
{
    Task<Result<IReadOnlyList<Comment>>> GetCommentsAsync(string targetId, CancellationToken cancellationToken = default);
    Task<Result<Comment>> AddCommentAsync(Comment comment, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteCommentAsync(Guid commentId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<Annotation>>> GetAnnotationsAsync(string targetId, CancellationToken cancellationToken = default);
    Task<Result<Annotation>> AddAnnotationAsync(Annotation annotation, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAnnotationAsync(Guid annotationId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<Bookmark>>> GetBookmarksAsync(string userId, CancellationToken cancellationToken = default);
    Task<Result<Bookmark>> AddBookmarkAsync(Bookmark bookmark, CancellationToken cancellationToken = default);
    Task<Result<bool>> RemoveBookmarkAsync(Guid bookmarkId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<Collection>>> GetCollectionsAsync(string userId, CancellationToken cancellationToken = default);
    Task<Result<Collection>> CreateCollectionAsync(Collection collection, CancellationToken cancellationToken = default);
    Task<Result<Collection>> UpdateCollectionAsync(Collection collection, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SharedWorkspace>>> GetWorkspacesAsync(string userId, CancellationToken cancellationToken = default);
    Task<Result<SharedWorkspace>> CreateWorkspaceAsync(SharedWorkspace workspace, CancellationToken cancellationToken = default);
    Task<Result<SharedWorkspace>> UpdateWorkspaceAsync(SharedWorkspace workspace, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<Result<bool>> ShareWorkspaceAsync(Guid workspaceId, string userId, string role, CancellationToken cancellationToken = default);
}
