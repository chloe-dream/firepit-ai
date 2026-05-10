namespace Firepit.Core.Terminal;

public interface ITerminalView : IDisposable
{
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
