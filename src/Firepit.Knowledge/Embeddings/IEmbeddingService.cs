namespace Firepit.Knowledge.Embeddings;

public interface IEmbeddingService
{
    int Dimensions { get; }

    /// <summary>
    /// Embeds <paramref name="text"/> into an L2-normalised vector. Throws
    /// <see cref="EmbeddingUnavailableException"/> while the model is still
    /// downloading (or failed to download) — callers degrade to FTS-only.
    /// </summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}
