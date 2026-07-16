using Firepit.Knowledge.Embeddings;

namespace Firepit.Knowledge.Tests;

/// <summary>
/// Deterministic bag-of-words embedding: each word hashes to one of the 384
/// dimensions. Texts sharing words land close in cosine space, which is all
/// the hybrid-search tests need. Flip <see cref="Unavailable"/> to simulate
/// the model still downloading.
/// </summary>
internal sealed class FakeEmbeddingService : IEmbeddingService
{
    public const int Dim = 384;

    public bool Unavailable { get; set; }

    public int Dimensions => Dim;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (Unavailable)
        {
            throw new EmbeddingUnavailableException("fake: model not ready");
        }

        var vec = new float[Dim];
        foreach (var word in (text ?? string.Empty).Split(
            [' ', '\n', '\r', '\t', '.', ',', '#'], StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = Math.Abs(GetStableHash(word.ToLowerInvariant())) % Dim;
            vec[idx] += 1f;
        }

        var norm = Math.Sqrt(Math.Max(vec.Sum(v => (double)v * v), 1e-18));
        for (var i = 0; i < Dim; i++)
        {
            vec[i] = (float)(vec[i] / norm);
        }

        return Task.FromResult(vec);
    }

    // string.GetHashCode is randomised per process — tests want stable
    // vectors across the index/search halves of a case, which a per-process
    // seed satisfies, but stable-across-runs makes failures reproducible.
    private static int GetStableHash(string s)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in s)
            {
                hash = (hash * 31) + c;
            }

            return hash;
        }
    }
}
