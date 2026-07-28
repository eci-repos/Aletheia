using Aletheia.Foundation.Shared;
using Microsoft.AspNetCore.Authorization;
using Aletheia.Repository.Abstractions.Interfaces;
using Aletheia.Repository.Abstractions.Models;
using Microsoft.AspNetCore.Mvc;

namespace Repository.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CollaborationController : ControllerBase
{
    private readonly ICollaborationService _collaboration;

    public CollaborationController(ICollaborationService collaboration)
    {
        _collaboration = collaboration ?? throw new ArgumentNullException(nameof(collaboration));
    }

    // Comments
    [HttpGet("comments")]
    public async Task<ActionResult<IReadOnlyList<Comment>>> GetComments(string targetId, CancellationToken cancellationToken)
    {
        var result = await _collaboration.GetCommentsAsync(targetId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("comments")]
    public async Task<ActionResult<Comment>> AddComment([FromBody] Comment comment, CancellationToken cancellationToken)
    {
        if (comment is null) return BadRequest(new { error = "Comment is required." });
        var result = await _collaboration.AddCommentAsync(comment, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpDelete("comments/{commentId}")]
    public async Task<ActionResult<bool>> DeleteComment(Guid commentId, CancellationToken cancellationToken)
    {
        var result = await _collaboration.DeleteCommentAsync(commentId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    // Annotations
    [HttpGet("annotations")]
    public async Task<ActionResult<IReadOnlyList<Annotation>>> GetAnnotations(string targetId, CancellationToken cancellationToken)
    {
        var result = await _collaboration.GetAnnotationsAsync(targetId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("annotations")]
    public async Task<ActionResult<Annotation>> AddAnnotation([FromBody] Annotation annotation, CancellationToken cancellationToken)
    {
        if (annotation is null) return BadRequest(new { error = "Annotation is required." });
        var result = await _collaboration.AddAnnotationAsync(annotation, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpDelete("annotations/{annotationId}")]
    public async Task<ActionResult<bool>> DeleteAnnotation(Guid annotationId, CancellationToken cancellationToken)
    {
        var result = await _collaboration.DeleteAnnotationAsync(annotationId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    // Bookmarks
    [HttpGet("bookmarks")]
    public async Task<ActionResult<IReadOnlyList<Bookmark>>> GetBookmarks(string userId, CancellationToken cancellationToken)
    {
        var result = await _collaboration.GetBookmarksAsync(userId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("bookmarks")]
    public async Task<ActionResult<Bookmark>> AddBookmark([FromBody] Bookmark bookmark, CancellationToken cancellationToken)
    {
        if (bookmark is null) return BadRequest(new { error = "Bookmark is required." });
        var result = await _collaboration.AddBookmarkAsync(bookmark, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpDelete("bookmarks/{bookmarkId}")]
    public async Task<ActionResult<bool>> RemoveBookmark(Guid bookmarkId, CancellationToken cancellationToken)
    {
        var result = await _collaboration.RemoveBookmarkAsync(bookmarkId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    // Collections
    [HttpGet("collections")]
    public async Task<ActionResult<IReadOnlyList<Collection>>> GetCollections(string userId, CancellationToken cancellationToken)
    {
        var result = await _collaboration.GetCollectionsAsync(userId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("collections")]
    public async Task<ActionResult<Collection>> CreateCollection([FromBody] Collection collection, CancellationToken cancellationToken)
    {
        if (collection is null) return BadRequest(new { error = "Collection is required." });
        var result = await _collaboration.CreateCollectionAsync(collection, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPut("collections")]
    public async Task<ActionResult<Collection>> UpdateCollection([FromBody] Collection collection, CancellationToken cancellationToken)
    {
        if (collection is null) return BadRequest(new { error = "Collection is required." });
        var result = await _collaboration.UpdateCollectionAsync(collection, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpDelete("collections/{collectionId}")]
    public async Task<ActionResult<bool>> DeleteCollection(Guid collectionId, CancellationToken cancellationToken)
    {
        var result = await _collaboration.DeleteCollectionAsync(collectionId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    // Workspaces
    [HttpGet("workspaces")]
    public async Task<ActionResult<IReadOnlyList<SharedWorkspace>>> GetWorkspaces(string userId, CancellationToken cancellationToken)
    {
        var result = await _collaboration.GetWorkspacesAsync(userId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("workspaces")]
    public async Task<ActionResult<SharedWorkspace>> CreateWorkspace([FromBody] SharedWorkspace workspace, CancellationToken cancellationToken)
    {
        if (workspace is null) return BadRequest(new { error = "Workspace is required." });
        var result = await _collaboration.CreateWorkspaceAsync(workspace, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPut("workspaces")]
    public async Task<ActionResult<SharedWorkspace>> UpdateWorkspace([FromBody] SharedWorkspace workspace, CancellationToken cancellationToken)
    {
        if (workspace is null) return BadRequest(new { error = "Workspace is required." });
        var result = await _collaboration.UpdateWorkspaceAsync(workspace, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpDelete("workspaces/{workspaceId}")]
    public async Task<ActionResult<bool>> DeleteWorkspace(Guid workspaceId, CancellationToken cancellationToken)
    {
        var result = await _collaboration.DeleteWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    [HttpPost("workspaces/{workspaceId}/share")]
    public async Task<ActionResult<bool>> ShareWorkspace(Guid workspaceId, [FromBody] SharePayload payload, CancellationToken cancellationToken)
    {
        if (payload is null) return BadRequest(new { error = "Payload is required." });
        var result = await _collaboration.ShareWorkspaceAsync(workspaceId, payload.UserId, payload.Role, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, new { error = result.Error });
    }

    public class SharePayload
    {
        public string UserId { get; set; } = string.Empty;
        public string Role { get; set; } = "viewer";
    }
}
