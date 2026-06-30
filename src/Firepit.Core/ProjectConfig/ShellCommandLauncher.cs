namespace Firepit.Core.ProjectConfig;

/// <summary>
/// Builds the cmd.exe wrapper that implements a shell command's
/// <see cref="ProjectCommand.KeepOpenOnError"/> behaviour: run the command,
/// close the console on success, keep it open (pause) on a non-zero exit so
/// the user can read the error. This replaces the blanket <c>-NoExit</c> /
/// <c>; pause</c> boilerplate that individual config files used to carry.
/// </summary>
public static class ShellCommandLauncher
{
    /// <summary>The wrapper host. Launched with <see cref="BuildKeepOpenOnErrorArguments"/>.</summary>
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
    /// Build the argument string for <see cref="ShellExecutable"/> that runs
    /// the command and pauses only on a non-zero (positive) exit code.
    /// </summary>
    /// <remarks>
    /// <c>/d</c> skips any AutoRun registry command. The inner command line is
    /// NOT wrapped in outer quotes, so the user's own quoting reaches cmd
    /// verbatim; the trailing <c>&amp; if errorlevel 1 pause</c> is the
    /// conditional hold. <c>if errorlevel 1</c> is true for exit codes ≥ 1 —
    /// the standard idiom that build/test/lint failures hit.
    /// </remarks>
    public static string BuildKeepOpenOnErrorArguments(string command, IReadOnlyList<string>? args)
    {
        var inner = BuildInnerCommandLine(command, args);
        return $"/d /c {inner} & if errorlevel 1 pause";
    }
}
