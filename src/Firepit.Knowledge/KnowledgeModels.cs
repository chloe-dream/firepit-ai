namespace Firepit.Knowledge;

/// <summary>One search hit, already deduplicated to document granularity.</summary>
public sealed record KnowledgeHit(
    string Scope,
    string Path,
    string Title,
    string? Heading,
    string Snippet,
    double Score);

/// <summary>
/// Result of a (possibly multi-scope) search. <see cref="Degraded"/> is true
/// when the vector side was unavailable in at least one scope and ranking
/// fell back to FTS-only there.
/// </summary>
public sealed record KnowledgeSearchResult(
    IReadOnlyList<KnowledgeHit> Hits,
    bool Degraded);

/// <summary>A full knowledge document as stored on disk.</summary>
public sealed record KnowledgeDocument(
    string Scope,
    string Path,
    string Title,
    string Content);

/// <summary>Outcome of one indexer pass over a scope.</summary>
public sealed record IndexStats(
    int Indexed,
    int Unchanged,
    int Removed,
    int PendingEmbedding)
{
    public static readonly IndexStats Empty = new(0, 0, 0, 0);

    public bool ChangedAnything => Indexed > 0 || Removed > 0;
}
