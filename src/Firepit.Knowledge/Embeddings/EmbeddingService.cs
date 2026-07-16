using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Firepit.Knowledge.Embeddings;

// Facade over MiniLmPipeline with lazy, thread-safe init tied to the
// ModelDownloader's readiness signal. One instance per app — the ONNX
// session is heavy; reuse it for the app lifetime. Ported from the-fishbowl
// (Fishbowl.Search.EmbeddingService).
public sealed class EmbeddingService : IEmbeddingService, IDisposable
{
    private readonly ModelDownloader _downloader;
    private readonly ILogger<EmbeddingService> _logger;
    private readonly Lock _gate = new();
    private MiniLmPipeline? _pipeline;
    private bool _disposed;

    public EmbeddingService(ModelDownloader downloader, ILogger<EmbeddingService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        _downloader = downloader;
        _logger = logger ?? NullLogger<EmbeddingService>.Instance;
    }

    public int Dimensions => MiniLmPipeline.EmbeddingDim;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var pipeline = GetOrInit();
        // ORT's Run() is synchronous but thread-safe. Hand back a completed
        // Task so the interface is async-friendly and callers can move off
        // the hot path later if they want.
        return Task.FromResult(pipeline.Embed(text));
    }

    private MiniLmPipeline GetOrInit()
    {
        var p = _pipeline;
        if (p is not null)
        {
            return p;
        }

        lock (_gate)
        {
            if (_pipeline is not null)
            {
                return _pipeline;
            }

            if (!_downloader.IsReady())
            {
                throw new EmbeddingUnavailableException(
                    "MiniLM-L6-v2 model isn't on disk yet. The first-run " +
                    "download is still in progress or previously failed.");
            }

            try
            {
                _pipeline = new MiniLmPipeline(_downloader.ModelPath, _downloader.VocabPath);
                _logger.LogInformation(
                    "MiniLmPipeline initialised from {Dir}",
                    Path.GetDirectoryName(_downloader.ModelPath));
                return _pipeline;
            }
            catch (Exception ex)
            {
                throw new EmbeddingUnavailableException(
                    "Failed to initialise MiniLmPipeline — see inner exception", ex);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _pipeline?.Dispose();
        _disposed = true;
    }
}
