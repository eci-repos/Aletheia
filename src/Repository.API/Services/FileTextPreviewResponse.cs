using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.Repository.API.Services;

/// <summary>
/// JSON payload for the preview endpoint's extracted-text renderer (non-PDF types).
/// PDFs stream the raw blob instead; unsupported types get a 415.
/// </summary>
public sealed record FileTextPreviewResponse(
    string FileName,
    string ContentType,
    string Text,
    IReadOnlyList<TextPage>? Pages);
