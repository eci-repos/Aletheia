using System.Collections.Concurrent;
using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Application.UseCases.Collaboration;

public sealed class CollaborationService : ICollaborationService
{
    private readonly ConcurrentDictionary<Guid, Comment> _comments = new();
    private readonly ConcurrentDictionary<Guid, Annotation> _annotations = new();
    private readonly ConcurrentDictionary<Guid, Bookmark> _bookmarks = new();
    private readonly ConcurrentDictionary<Guid, Collection> _collections = new();
    private readonly ConcurrentDictionary<Guid, SharedWorkspace> _workspaces = new();

    public Task<Result<IReadOnlyList<Comment>>> GetCommentsAsync(string targetId, CancellationToken cancellationToken = default)
    {
        var comments = _comments.Values.Where(c => c.TargetId == targetId).OrderBy(c => c.CreatedAt).ToList();
        return Task.FromResult(Result<IReadOnlyList<Comment>>.Success(comments));
    }

    public Task<Result<Comment>> AddCommentAsync(Comment comment, CancellationToken cancellationToken = default)
    {
        if (comment is null)
        {
            throw new ArgumentNullException(nameof(comment));
        }

        comment.Id = Guid.NewGuid();
        comment.CreatedAt = DateTimeOffset.UtcNow;
        comment.UpdatedAt = DateTimeOffset.UtcNow;
        _comments[comment.Id] = comment;
        return Task.FromResult(Result<Comment>.Success(comment));
    }

    public Task<Result<bool>> DeleteCommentAsync(Guid commentId, CancellationToken cancellationToken = default)
    {
        var removed = _comments.TryRemove(commentId, out _);
        return Task.FromResult(Result<bool>.Success(removed));
    }

    public Task<Result<IReadOnlyList<Annotation>>> GetAnnotationsAsync(string targetId, CancellationToken cancellationToken = default)
    {
        var annotations = _annotations.Values.Where(a => a.TargetId == targetId).OrderBy(a => a.CreatedAt).ToList();
        return Task.FromResult(Result<IReadOnlyList<Annotation>>.Success(annotations));
    }

    public Task<Result<Annotation>> AddAnnotationAsync(Annotation annotation, CancellationToken cancellationToken = default)
    {
        if (annotation is null)
        {
            throw new ArgumentNullException(nameof(annotation));
        }

        annotation.Id = Guid.NewGuid();
        annotation.CreatedAt = DateTimeOffset.UtcNow;
        _annotations[annotation.Id] = annotation;
        return Task.FromResult(Result<Annotation>.Success(annotation));
    }

    public Task<Result<bool>> DeleteAnnotationAsync(Guid annotationId, CancellationToken cancellationToken = default)
    {
        var removed = _annotations.TryRemove(annotationId, out _);
        return Task.FromResult(Result<bool>.Success(removed));
    }

    public Task<Result<IReadOnlyList<Bookmark>>> GetBookmarksAsync(string userId, CancellationToken cancellationToken = default)
    {
        var bookmarks = _bookmarks.Values.Where(b => b.UserId == userId).OrderBy(b => b.CreatedAt).ToList();
        return Task.FromResult(Result<IReadOnlyList<Bookmark>>.Success(bookmarks));
    }

    public Task<Result<Bookmark>> AddBookmarkAsync(Bookmark bookmark, CancellationToken cancellationToken = default)
    {
        if (bookmark is null)
        {
            throw new ArgumentNullException(nameof(bookmark));
        }

        bookmark.Id = Guid.NewGuid();
        bookmark.CreatedAt = DateTimeOffset.UtcNow;
        _bookmarks[bookmark.Id] = bookmark;
        return Task.FromResult(Result<Bookmark>.Success(bookmark));
    }

    public Task<Result<bool>> RemoveBookmarkAsync(Guid bookmarkId, CancellationToken cancellationToken = default)
    {
        var removed = _bookmarks.TryRemove(bookmarkId, out _);
        return Task.FromResult(Result<bool>.Success(removed));
    }

    public Task<Result<IReadOnlyList<Collection>>> GetCollectionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var collections = _collections.Values.Where(c => c.OwnerId == userId).OrderBy(c => c.CreatedAt).ToList();
        return Task.FromResult(Result<IReadOnlyList<Collection>>.Success(collections));
    }

    public Task<Result<Collection>> CreateCollectionAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        if (collection is null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        collection.Id = Guid.NewGuid();
        collection.CreatedAt = DateTimeOffset.UtcNow;
        collection.UpdatedAt = DateTimeOffset.UtcNow;
        _collections[collection.Id] = collection;
        return Task.FromResult(Result<Collection>.Success(collection));
    }

    public Task<Result<Collection>> UpdateCollectionAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        if (collection is null || !_collections.ContainsKey(collection.Id))
        {
            throw new ArgumentException("Collection not found.", nameof(collection));
        }

        collection.UpdatedAt = DateTimeOffset.UtcNow;
        _collections[collection.Id] = collection;
        return Task.FromResult(Result<Collection>.Success(collection));
    }

    public Task<Result<bool>> DeleteCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        var removed = _collections.TryRemove(collectionId, out _);
        return Task.FromResult(Result<bool>.Success(removed));
    }

    public Task<Result<IReadOnlyList<SharedWorkspace>>> GetWorkspacesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var workspaces = _workspaces.Values
            .Where(w => w.OwnerId == userId || w.Members.Any(m => m.UserId == userId))
            .OrderBy(w => w.CreatedAt)
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<SharedWorkspace>>.Success(workspaces));
    }

    public Task<Result<SharedWorkspace>> CreateWorkspaceAsync(SharedWorkspace workspace, CancellationToken cancellationToken = default)
    {
        if (workspace is null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        workspace.Id = Guid.NewGuid();
        workspace.CreatedAt = DateTimeOffset.UtcNow;
        workspace.UpdatedAt = DateTimeOffset.UtcNow;
        _workspaces[workspace.Id] = workspace;
        return Task.FromResult(Result<SharedWorkspace>.Success(workspace));
    }

    public Task<Result<SharedWorkspace>> UpdateWorkspaceAsync(SharedWorkspace workspace, CancellationToken cancellationToken = default)
    {
        if (workspace is null || !_workspaces.ContainsKey(workspace.Id))
        {
            throw new ArgumentException("Workspace not found.", nameof(workspace));
        }

        workspace.UpdatedAt = DateTimeOffset.UtcNow;
        _workspaces[workspace.Id] = workspace;
        return Task.FromResult(Result<SharedWorkspace>.Success(workspace));
    }

    public Task<Result<bool>> DeleteWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var removed = _workspaces.TryRemove(workspaceId, out _);
        return Task.FromResult(Result<bool>.Success(removed));
    }

    public Task<Result<bool>> ShareWorkspaceAsync(Guid workspaceId, string userId, string role, CancellationToken cancellationToken = default)
    {
        if (!_workspaces.TryGetValue(workspaceId, out var workspace))
        {
            return Task.FromResult(Result<bool>.Success(false));
        }

        var existing = workspace.Members.FirstOrDefault(m => m.UserId == userId);
        if (existing is not null)
        {
            existing.Role = role;
        }
        else
        {
            workspace.Members.Add(new WorkspaceMember { UserId = userId, Role = role });
        }

        workspace.UpdatedAt = DateTimeOffset.UtcNow;
        return Task.FromResult(Result<bool>.Success(true));
    }
}
