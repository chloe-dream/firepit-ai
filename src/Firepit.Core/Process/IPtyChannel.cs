namespace Firepit.Core.Process;

public interface IPtyChannel : IAsyncDisposable
{
    int Pid { get; }

    Task<int> WaitForExitAsync(CancellationToken ct);

    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAsync(CancellationToken ct);

    void Resize(int cols, int rows);
}
