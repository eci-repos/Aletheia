using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Models;

namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>Resolves knowledge themes (Sprint 58) to registered sources and exposes theme counts.</summary>
public interface IKnowledgeThemeService
{
    /// <summary>Returns the registered source ids whose theme is in <paramref name="themes"/> (fallback derivation included). Empty when no themes match or none requested.</summary>
    Task<Result<IReadOnlyList<Guid>>> ResolveSourceIdsAsync(IReadOnlyList<string> themes, CancellationToken cancellationToken = default);

    /// <summary>Returns every known theme with the number of registered documents that fall under it (registry themes with zero documents included).</summary>
    Task<Result<IReadOnlyList<KnowledgeThemeCount>>> GetThemesWithCountsAsync(CancellationToken cancellationToken = default);
}