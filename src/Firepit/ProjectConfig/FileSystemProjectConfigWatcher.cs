using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Firepit.Core.ProjectConfig;
using Serilog;

namespace Firepit.ProjectConfig;

/// <summary>
/// Watches <c>&lt;projectPath&gt;/.firepit/config.json</c> via
/// <see cref="FileSystemWatcher"/>. Debounces ~500 ms before re-parsing so an
/// editor's swap-file pattern (write .tmp → rename) collapses to one event.
/// Parse failures are logged + dropped so partial saves don't propagate.
/// </summary>
public sealed class FileSystemProjectConfigWatcher : IProjectConfigWatcher
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly IProjectConfigStore _store;
    private readonly Dispatcher _dispatcher;
    private readonly FileSystemWatcher _fsw;
    private readonly object _gate = new();
    private CancellationTokenSource? _pendingCts;
    private bool _started;
    private bool _disposed;

    public FileSystemProjectConfigWatcher(string projectPath, IProjectConfigStore store, Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ProjectPath = projectPath;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        var dir = Path.Combine(projectPath, JsonProjectConfigStore.DirectoryName);
        // Watch the .firepit directory rather than the file directly — VS Code
        // and friends rename a .tmp file in place, which doesn't fire Changed
        // on the original. Filter narrows to config.json events only.
        _fsw = new FileSystemWatcher(dir, JsonProjectConfigStore.FileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        _fsw.Changed += OnFsEvent;
        _fsw.Created += OnFsEvent;
        _fsw.Renamed += OnFsEvent;
    }

    public string ProjectPath { get; }

    public event EventHandler<Firepit.Core.ProjectConfig.ProjectConfig>? ConfigChanged;

    public void Start()
    {
        if (_disposed || _started) return;
        try
        {
            // Directory may not exist yet (project hasn't grown a .firepit/
            // folder). Create it so the watcher has something to watch.
            Directory.CreateDirectory(_fsw.Path);
            _fsw.EnableRaisingEvents = true;
            _started = true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not start config watcher for {Path}", ProjectPath);
        }
    }

    public void Stop()
    {
        if (!_started) return;
        try { _fsw.EnableRaisingEvents = false; } catch { /* ignored */ }
        _started = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        try { _pendingCts?.Cancel(); } catch { /* ignored */ }
        _pendingCts?.Dispose();
        _fsw.Changed -= OnFsEvent;
        _fsw.Created -= OnFsEvent;
        _fsw.Renamed -= OnFsEvent;
        _fsw.Dispose();
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        // Debounce: cancel any pending re-read, schedule a fresh one. Editors
        // emit several events per save (truncate + write + rename); collapsing
        // them avoids re-parsing in the middle of a write.
        CancellationTokenSource cts;
        lock (_gate)
        {
            try { _pendingCts?.Cancel(); } catch { /* ignored */ }
            _pendingCts?.Dispose();
            _pendingCts = new CancellationTokenSource();
            cts = _pendingCts;
        }
        _ = ProcessAfterDelayAsync(cts.Token);
    }

    private async Task ProcessAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(DebounceDelay, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException) { return; }
        if (ct.IsCancellationRequested || _disposed) return;

        Firepit.Core.ProjectConfig.ProjectConfig? loaded;
        try
        {
            loaded = _store.Load(ProjectPath);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Reload failed for {Path}; will retry on next event", ProjectPath);
            return;
        }
        if (loaded is null)
        {
            // File deleted or moved — treat as reset to defaults. Consumer
            // decides what to do (re-resolve from globals).
            loaded = new Firepit.Core.ProjectConfig.ProjectConfig();
        }

        var snapshot = loaded;
        await _dispatcher.InvokeAsync(() => ConfigChanged?.Invoke(this, snapshot));
    }
}
