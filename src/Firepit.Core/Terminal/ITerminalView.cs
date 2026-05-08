namespace Firepit.Core.Terminal;

public interface ITerminalView : IDisposable
{
    Task InitializeAsync(CancellationToken ct);

    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

    event EventHandler<ReadOnlyMemory<byte>> InputReceived;

    event EventHandler<TerminalSize> Resized;

    void Focus();
}
