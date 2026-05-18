using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Firepit.Core.ProjectConfig;
using Serilog;
// Alias keeps things readable around the Firepit.Process namespace clash —
// System.Diagnostics.Process is the .NET host-process API, our own Process
// project hosts the PTY layer.
using SysProcess = System.Diagnostics.Process;
using SysProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace Firepit.Views;

/// <summary>
/// Per-<see cref="SessionTab"/> lifecycle store for shell commands that opt
/// into <c>window = "reuse:&lt;id&gt;"</c> or <c>longRunning = true</c>. Holds
/// a single live OS process per reuse-id (or per command name for fire-and-
/// forget long-running commands), surfaces is-alive queries to the toolbar,
/// focuses an existing window via P/Invoke, and kills the process tree on
/// Stop. Fire-and-forget one-shots (<c>window = "new"</c> with
/// <c>longRunning != true</c>) skip the registry entirely — they were the
/// only Phase-A behaviour and stay zero-overhead.
/// </summary>
internal sealed class CommandRunner : IDisposable
{
    private readonly Dictionary<string, LiveCommand> _live = new(StringComparer.Ordinal);
    private readonly Action _stateChanged;
    private readonly object _gate = new();
    private bool _disposed;

    public CommandRunner(Action stateChanged)
    {
        _stateChanged = stateChanged;
    }

    /// <summary>
    /// True when the command's tracking key (reuse-id or longRunning name)
    /// still points at a live OS process. Reaping happens lazily here — an
    /// exited process is removed and the answer is false.
    /// </summary>
    public bool IsAlive(ProjectCommand cmd)
    {
        var key = KeyOf(cmd);
        if (key is null) return false;
        lock (_gate)
        {
            if (!_live.TryGetValue(key, out var live)) return false;
            try
            {
                if (!live.Proc.HasExited) return true;
            }
            catch (InvalidOperationException)
            {
                // Process handle gone — treat as exited.
            }
            _live.Remove(key);
            return false;
        }
    }

    /// <summary>
    /// Bring the existing tracked process's main window to the foreground.
    /// No-op if not alive, or if the process has no window handle yet
    /// (headless or still starting up).
    /// </summary>
    public bool FocusExisting(ProjectCommand cmd)
    {
        var key = KeyOf(cmd);
        if (key is null) return false;
        SysProcess? proc;
        lock (_gate)
        {
            if (!_live.TryGetValue(key, out var live)) return false;
            proc = live.Proc;
        }
        try
        {
            // MainWindowHandle is populated lazily and can be IntPtr.Zero
            // before the console window is up. Refresh forces a re-query.
            proc.Refresh();
            var hwnd = proc.MainWindowHandle;
            if (hwnd == IntPtr.Zero) return false;
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_RESTORE);
            }
            return SetForegroundWindow(hwnd);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Focus existing failed for command '{Name}'", cmd.Name);
            return false;
        }
    }

    /// <summary>
    /// Start the command using the prepared <see cref="SysProcessStartInfo"/>.
    /// Registers the resulting process if tracked (<see cref="KeyOf"/> returns
    /// non-null); otherwise fires-and-forgets. Returns the spawned process
    /// when one was produced — callers shouldn't dispose it, the runner owns
    /// lifetime.
    /// </summary>
    public SysProcess? Spawn(ProjectCommand cmd, SysProcessStartInfo psi)
    {
        var proc = SysProcess.Start(psi);
        if (proc is null) return null;

        var key = KeyOf(cmd);
        if (key is null)
        {
            // Untracked — drop the handle, Windows reaps on exit.
            return proc;
        }

        try { proc.EnableRaisingEvents = true; }
        catch (Exception ex) { Log.Debug(ex, "EnableRaisingEvents failed for '{Name}'", cmd.Name); }

        proc.Exited += (_, _) =>
        {
            lock (_gate)
            {
                if (_live.TryGetValue(key, out var existing) && ReferenceEquals(existing.Proc, proc))
                {
                    _live.Remove(key);
                }
            }
            try { _stateChanged(); } catch { /* ignored */ }
        };

        lock (_gate)
        {
            // Replace any stale entry (HasExited would already have removed
            // it, but be defensive: a redundant key wins the newer process).
            if (_live.TryGetValue(key, out var prior))
            {
                try { prior.Proc.Dispose(); } catch { /* ignored */ }
            }
            _live[key] = new LiveCommand(cmd, proc);
        }
        try { _stateChanged(); } catch { /* ignored */ }
        return proc;
    }

    /// <summary>
    /// Kill the tracked process tree if alive. Safe to call when nothing is
    /// running. UAC-elevated children can't be killed by the non-elevated
    /// Firepit parent — surfaced as a debug log; the entry stays registered
    /// until the child eventually exits.
    ///
    /// Strategy: <see cref="SysProcess.Kill"/> with <c>entireProcessTree</c>
    /// only walks .NET-known descendants, which often misses grandchildren
    /// spawned through cmd.exe / shell wrappers (npm → node → vite is the
    /// canonical case). We follow up with <c>taskkill /F /T /PID</c> which
    /// uses the kernel's process tree, catching what .NET misses.
    /// </summary>
    public void Stop(ProjectCommand cmd)
    {
        var key = KeyOf(cmd);
        if (key is null) return;
        SysProcess? proc;
        lock (_gate)
        {
            if (!_live.TryGetValue(key, out var live)) return;
            proc = live.Proc;
        }
        int pid;
        try { pid = proc.Id; }
        catch (Exception ex) { Log.Debug(ex, "Stop: process id unavailable for '{Name}'", cmd.Name); return; }

        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Stop primary Kill failed for command '{Name}' — likely elevated child", cmd.Name);
        }

        // taskkill mops up grandchildren the .NET process-tree walk missed
        // (shell-launched dev servers spawn a tree the parent doesn't track).
        // Fire-and-forget; failures are fine — process may already be down.
        try
        {
            var killPsi = new SysProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = $"/F /T /PID {pid}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var kill = SysProcess.Start(killPsi);
            kill?.WaitForExit(2000);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Stop taskkill fallback failed for '{Name}' (pid {Pid})", cmd.Name, pid);
        }

        Log.Information("Stopped command '{Name}' (pid {Pid})", cmd.Name, pid);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Intentionally do NOT kill children on tab close. The user opened
        // these long-running watchers deliberately (a relay proxy, a dev
        // server); the Firepit tab going away shouldn't take them down.
        // The process handles leak until the OS reaps them — fine for the
        // lifetime of the Firepit window.
        lock (_gate) { _live.Clear(); }
    }

    /// <summary>
    /// Tracking key. Null = untracked fire-and-forget.
    ///   reuse:&lt;id&gt;  → "reuse:&lt;id&gt;"
    ///   longRunning   → "name:&lt;commandName&gt;"
    ///   neither       → null
    /// </summary>
    internal static string? KeyOf(ProjectCommand cmd)
    {
        if (TryParseReuse(cmd.Window, out var id)) return "reuse:" + id;
        if (cmd.LongRunning == true) return "name:" + cmd.Name;
        return null;
    }

    /// <summary>
    /// Parse <c>"reuse:&lt;id&gt;"</c>. Returns false (and empty id) for any
    /// other string, including a literal <c>"reuse:"</c> with no id.
    /// </summary>
    public static bool TryParseReuse(string? window, out string id)
    {
        id = string.Empty;
        if (string.IsNullOrEmpty(window)) return false;
        const string prefix = "reuse:";
        if (!window.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var rest = window.AsSpan(prefix.Length).Trim();
        if (rest.Length == 0) return false;
        id = rest.ToString();
        return true;
    }

    private sealed record LiveCommand(ProjectCommand Command, SysProcess Proc);

    // --- Win32 ---------------------------------------------------------------

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
