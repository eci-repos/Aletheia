namespace Aletheia.RAGS.Abstractions.Configuration;

/// <summary>
/// Feature flags read from the <c>FeatureFlags</c> configuration section.
/// </summary>
public sealed class FeatureFlagsOptions
{
    public const string SectionName = "FeatureFlags";

    /// <summary>
    /// When <c>false</c> (default), internal retrieval surfaces (raw Wiki/WRAGS,
    /// GraphRAG, LazyGraphRAG, global-graph search) are hidden from end users.
    /// The user-facing Wiki surface (document briefs) and semantic search remain visible.
    /// </summary>
    public bool ShowInternalSearch { get; set; }
}
