namespace Aletheia.RAGS.Abstractions.Models;

public sealed class WikiPageEditRequest
{
    public string? Title { get; set; }

    public string? Summary { get; set; }

    public IReadOnlyList<string>? RelatedTopics { get; set; }

    public string? Status { get; set; }

    public string? EditedBy { get; set; }

    public string? ChangeNote { get; set; }
}
