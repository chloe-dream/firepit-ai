using System.Text;

namespace Firepit.Core.ProjectConfig;

/// <summary>
/// Builds the cmd.exe wrapper used for shell toolbar commands that need more
/// than a bare launch:
/// <list type="bullet">
///   <item><see cref="ProjectCommand.KeepOpenOnError"/> — close the console on
///   success, pause on a non-zero exit so the error is readable.</item>
///   <item>elevated (<see cref="ProjectCommand.Elevated"/>) spawns — Windows'
///   ShellExecute ignores <c>WorkingDirectory</c> for a <c>runas</c> launch and
///   drops the child in <c>system32</c>, so a <c>cd /d</c> shim restores the
///   project root and relative paths resolve again.</item>
/// </list>
/// </summary>
public static class ShellCommandLauncher
{
    /// <summary>The wrapper host. Launched with <see cref="BuildWrappedArguments"/>.</summary>
    public const string ShellExecutable = "cmd.exe";

    /// <summary>
    /// Compose the inner command line exactly the way a direct launch would —
    /// the executable followed by space-joined args. The user owns their own
    /// quoting (same contract as a non-wrapped launch).
    /// </summary>
    public static string BuildInnerCommandLine(string command, IReadOnlyList<string>? args)
    {
        ArgumentException.ThrowIfNullOrEmpty(command);
        return args is { Count: > 0 }
            ? command + " " + string.Join(' ', args)
            : command;
    }

    /// <summary>
    /// Build the argument string for <see cref="ShellExecutable"/>.
    /// </summary>
    /// <param name="command">The executable to run.</param>
    /// <param name="args">Its arguments (space-joined verbatim).</param>
    /// <param name="workingDirectory">
    /// When non-empty, a <c>cd /d "&lt;dir&gt;"</c> shim runs first so the inner
    /// command starts there. Essential for elevated launches (ShellExecute
    /// ignores <c>WorkingDirectory</c> under <c>runas</c> and starts the child
    /// in system32); harmless for non-elevated ones where it's already honoured.
    /// </param>
    /// <param name="keepOpenOnError">
    /// Append <c>&amp; if errorlevel 1 pause</c> so the console stays open on a
    /// non-zero (positive) exit code — the standard build/test/lint failure idiom.
    /// </param>
    /// <remarks>
    /// <c>/d</c> skips any AutoRun registry command. The inner command line is
    /// NOT wrapped in outer quotes, so the user's own quoting reaches cmd
    /// verbatim. The <c>cd</c> uses <c>&amp;&amp;</c> so the command only runs
    /// once the directory change succeeds.
    /// </remarks>
    public static string BuildWrappedArguments(
        string command,
        IReadOnlyList<string>? args,
        string? workingDirectory,
        bool keepOpenOnError)
    {
        var inner = BuildInnerCommandLine(command, args);
        var sb = new StringBuilder("/d /c ");

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            var dir = workingDirectory;
            // Avoid a trailing separator immediately before the closing quote
            // (the \" sequence reads oddly), but keep a drive root's slash —
            // "C:\" must stay "C:\", not collapse to "C:".
            if (dir.Length > 3 && (dir[^1] == '\\' || dir[^1] == '/'))
            {
                dir = dir.TrimEnd('\\', '/');
            }
            sb.Append("cd /d \"").Append(dir).Append("\" && ");
        }

        sb.Append(inner);

        if (keepOpenOnError)
        {
            sb.Append(" & if errorlevel 1 pause");
        }
        return sb.ToString();
    }
}
