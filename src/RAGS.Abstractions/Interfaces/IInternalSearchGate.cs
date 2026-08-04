namespace Aletheia.RAGS.Abstractions.Interfaces;

/// <summary>
/// Gates internal retrieval surfaces (raw Wiki/WRAGS modes, GraphRAG, LazyGraphRAG,
/// global-graph search) behind an admin/diagnostics flag.
/// </summary>
public interface IInternalSearchGate
{
    /// <summary>True when internal search surfaces are enabled (admin/diagnostics mode).</summary>
    bool ShowInternalSearch { get; }
}
