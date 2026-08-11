namespace Aletheia.RAGS.Abstractions.Models;

/// <summary>
/// Selects which knowledge-indexer path runs during knowledge-source hydration.
/// </summary>
public enum KnowledgeIndexMode
{
    /// <summary>
    /// Full graph-intelligence indexing (LLM entity discovery, node summaries,
    /// relationship extraction, community detection + summaries). Used by repairs
    /// and chat hydration, which need a fully-derived graph.
    /// </summary>
    Full,

    /// <summary>
    /// Lightweight deterministic indexing (no LLM): topic extraction, taxonomy tags,
    /// ontology source/topic entities, and graph seed nodes with
    /// <c>lazyEnrichmentStatus = "Pending"</c>. Matches the file-upload path and is
    /// used by reembed, whose job is regenerating embeddings — not re-deriving graph
    /// intelligence, which is produced lazily during retrieval anyway.
    /// </summary>
    Lightweight
}
