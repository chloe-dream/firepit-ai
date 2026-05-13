using System.Globalization;

namespace Firepit.Core.Jobs;

/// <summary>
/// Builds the argv passed to <c>claude -p</c> for a given <see cref="JobRunRequest"/>.
/// Kept as a pure function so it can be unit-tested without spawning processes.
///
/// The runner shells out non-interactively; argv (not a command-line string) is
/// what <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> consumes
/// directly, so quoting is the framework's problem.
/// </summary>
public static class ClaudeJobArgBuilder
{
    public static IReadOnlyList<string> Build(JobRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var args = new List<string>
        {
            "-p",
            request.Prompt,
            "--output-format",
            "json",
        };

        if (request.AllowedTools is { Count: > 0 })
        {
            args.Add("--allowed-tools");
            args.Add(string.Join(",", request.AllowedTools));
        }

        if (request.MaxTurns is int turns)
        {
            args.Add("--max-turns");
            args.Add(turns.ToString(CultureInfo.InvariantCulture));
        }

        if (request.MaxBudgetUsd is decimal budget)
        {
            args.Add("--max-budget-usd");
            args.Add(budget.ToString(CultureInfo.InvariantCulture));
        }

        if (request.SkipPermissions)
        {
            args.Add("--dangerously-skip-permissions");
        }

        return args;
    }

    /// <summary>
    /// Render argv as a single human-readable line for the run record.
    /// Not a shell-safe quoting — display only.
    /// </summary>
    public static string Render(string executable, IReadOnlyList<string> argv)
    {
        var parts = new List<string>(argv.Count + 1) { executable };
        foreach (var arg in argv)
        {
            parts.Add(arg.Contains(' ') ? $"\"{arg}\"" : arg);
        }
        return string.Join(' ', parts);
    }
}
