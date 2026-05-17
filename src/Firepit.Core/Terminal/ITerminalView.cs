namespace Firepit.Core.Terminal;

public interface ITerminalView : IDisposable
{
    /// <summary>
    /// True once <see cref="InitializeAsync"/> has completed successfully.
    /// Stays false if init was cancelled or threw — letting callers detect
    /// a half-built view and dispose it before reuse (a CoreWebView2 left
    /// null after cancellation cannot accept any further posts).
    /// </summary>
    bool IsInitialized { get; }

    Task InitializeAsync(CancellationToken ct);

    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

    event EventHandler<ReadOnlyMemory<byte>> InputReceived;

    event EventHandler<TerminalSize> Resized;

    /// <summary>
    /// Raised when the agent emits an OSC 9;4 tab-progress sequence.
    /// True = work in progress (state 1/2/3/4), false = cleared (state 0).
    /// </summary>
    event EventHandler<bool> ProgressChanged;

    void Focus();
}
