namespace Aletheia.RAGS.Abstractions.Models;

/// <summary>One knowledge theme and how many registered documents currently fall under it.</summary>
public sealed record KnowledgeThemeCount(string Theme, int DocumentCount);