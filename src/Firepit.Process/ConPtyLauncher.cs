using Firepit.Core.Process;
using Firepit.Core.Terminal;
using Porta.Pty;

namespace Firepit.Process;

public static class ConPtyLauncher
{
    public static async Task<IPtyChannel> SpawnAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentOverrides,
        TerminalSize initialSize,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (environmentOverrides is not null)
        {
            foreach (var (key, value) in environmentOverrides)
            {
                environment[key] = value ?? string.Empty;
            }
        }

        var options = new PtyOptions
        {
            App = executable,
            CommandLine = arguments.ToArray(),
            Cwd = workingDirectory,
            Cols = initialSize.Cols,
            Rows = initialSize.Rows,
            Environment = environment,
        };

        var connection = await PtyProvider.SpawnAsync(options, ct).ConfigureAwait(false);
        return new ConPtyChannel(connection);
    }
}
