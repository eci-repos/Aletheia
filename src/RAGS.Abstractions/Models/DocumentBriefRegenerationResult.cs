namespace Aletheia.RAGS.Abstractions.Models;

/// <summary>Progress callback payload for document brief regeneration.</summary>
public sealed record DocumentBriefProgress(
    string Stage,
    string Detail,
    int Completed,
    int Total);

/// <summary>Summary of a document brief regeneration run.</summary>
public sealed record DocumentBriefRegenerationResult(
    int TotalDocuments,
    int Generated,
    IReadOnlyList<string> Skipped);
