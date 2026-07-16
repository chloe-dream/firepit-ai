using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;

namespace Firepit.Knowledge.Embeddings;

// Owns the ONNX InferenceSession and the WordPiece tokenizer for
// sentence-transformers/all-MiniLM-L6-v2. Load once, reuse forever — ORT
// sessions are thread-safe for Run() after construction. Dispose at app
// shutdown. Ported from the-fishbowl (Fishbowl.Search.MiniLmPipeline).
//
// Forward pass per query:
//   1. Tokenize → input_ids + attention_mask (padded/truncated to max 128).
//   2. Run the ONNX graph → token-level embeddings [1, seq, 384].
//   3. Mean-pool over tokens weighted by attention_mask → sentence embedding.
//   4. L2-normalise → cosine similarity collapses to dot product.
//
// These steps match the HuggingFace sentence-transformers reference
// implementation. Any deviation breaks semantic parity with other systems
// using the same model.
internal sealed class MiniLmPipeline : IDisposable
{
    private const int MaxSequenceLength = 128;
    public const int EmbeddingDim = 384;

    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private bool _disposed;

    public MiniLmPipeline(string modelPath, string vocabPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("ONNX model not found", modelPath);
        }

        if (!File.Exists(vocabPath))
        {
            throw new FileNotFoundException("Tokenizer vocab not found", vocabPath);
        }

        var options = new SessionOptions
        {
            // CPU only. GPU would need runtime selection + users shipping
            // CUDA. Embedding a chunk is already <50ms on CPU for 128 tokens.
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = Environment.ProcessorCount,
        };
        _session = new InferenceSession(modelPath, options);

        // BertTokenizer.Create loads a `vocab.txt` WordPiece vocabulary (one
        // token per line) — the format HuggingFace publishes for every
        // BERT-family model. `LowerCaseBeforeTokenization` matches MiniLM's
        // uncased configuration.
        using var vocabStream = File.OpenRead(vocabPath);
        _tokenizer = BertTokenizer.Create(
            vocabStream,
            new BertOptions
            {
                LowerCaseBeforeTokenization = true,
            });
    }

    public float[] Embed(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // BertTokenizer's max-count overload returns ids already truncated
        // to MaxSequenceLength (including the CLS/SEP special tokens).
        var ids = _tokenizer.EncodeToIds(
            text ?? string.Empty,
            maxTokenCount: MaxSequenceLength,
            normalizedText: out _,
            charsConsumed: out _);

        var seq = ids.Count;
        // attention_mask lets the model (and our pooler) ignore padding
        // tokens. For a perfectly-sized input it's all 1s, but we still
        // compute it so pooling below can stay branch-free.
        var inputIds = new long[seq];
        var attentionMask = new long[seq];
        var tokenTypeIds = new long[seq];
        for (var i = 0; i < seq; i++)
        {
            inputIds[i] = ids[i];
            attentionMask[i] = 1;
            tokenTypeIds[i] = 0;
        }

        var shape = new[] { 1L, (long)seq };
        using var idsTensor = OrtValue.CreateTensorValueFromMemory(inputIds, shape);
        using var maskTensor = OrtValue.CreateTensorValueFromMemory(attentionMask, shape);
        using var typesTensor = OrtValue.CreateTensorValueFromMemory(tokenTypeIds, shape);

        var inputs = new Dictionary<string, OrtValue>
        {
            ["input_ids"] = idsTensor,
            ["attention_mask"] = maskTensor,
            ["token_type_ids"] = typesTensor,
        };

        using var runOptions = new RunOptions();
        using var outputs = _session.Run(runOptions, inputs, _session.OutputNames);

        // MiniLM returns `last_hidden_state` of shape [1, seq, 384]. Older
        // exports call this "token_embeddings"; `outputs[0]` is the first
        // and only needed output in both cases.
        var tokenEmbeddings = outputs[0].GetTensorDataAsSpan<float>();

        // Mean pool: for each of the 384 dims, sum across tokens (weighted by
        // attention mask, which is all 1s for non-padding) and divide by the
        // number of non-padding tokens. Guarded against div-by-zero for the
        // degenerate empty-string case.
        var pooled = new float[EmbeddingDim];
        var maskSum = 0.0;
        for (var t = 0; t < seq; t++)
        {
            var mask = (float)attentionMask[t];
            maskSum += mask;
            var rowStart = t * EmbeddingDim;
            for (var d = 0; d < EmbeddingDim; d++)
            {
                pooled[d] += tokenEmbeddings[rowStart + d] * mask;
            }
        }

        var denom = (float)Math.Max(maskSum, 1e-9);
        for (var d = 0; d < EmbeddingDim; d++)
        {
            pooled[d] /= denom;
        }

        // L2 normalise: cosine similarity = dot product of unit vectors, so
        // downstream can skip the denominator at query time. Same guard as
        // above against the all-zeros edge case.
        var norm = 0.0;
        for (var d = 0; d < EmbeddingDim; d++)
        {
            norm += pooled[d] * pooled[d];
        }

        norm = Math.Sqrt(Math.Max(norm, 1e-18));
        var inv = (float)(1.0 / norm);
        for (var d = 0; d < EmbeddingDim; d++)
        {
            pooled[d] *= inv;
        }

        return pooled;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session.Dispose();
        _disposed = true;
    }
}
