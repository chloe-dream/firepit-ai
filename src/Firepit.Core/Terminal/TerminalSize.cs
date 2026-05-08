namespace Firepit.Core.Terminal;

public readonly record struct TerminalSize(int Cols, int Rows)
{
    public static readonly TerminalSize Default = new(80, 24);
}
