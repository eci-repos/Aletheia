namespace Aletheia.Repository.Abstractions.Models;

/// <summary>Theme information for one file_metadata row, used to build knowledge-theme counts and resolve themes to source ids.</summary>
public sealed record FileThemeRow(
    Guid FileId,
    string FileName,
    string? TemplateName,
    IReadOnlyList<string>? Theme,
    string? TemplateStatus = null);
