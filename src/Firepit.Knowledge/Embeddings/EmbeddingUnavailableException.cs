namespace Firepit.Knowledge.Embeddings;

// Thrown while the embedding model isn't usable (first-run download still in
// progress, download failed, or pipeline init failed). Callers on the hot
// path catch this and degrade gracefully: FTS-only search, chunks flagged
// as pending-embedding for a later sweep.
public sealed class EmbeddingUnavailableException : Exception
{
    public EmbeddingUnavailableException(string message)
        : base(message)
    {
    }

    public EmbeddingUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
