using System.IO;

namespace Firepit.Core.Inbox;

/// <summary>
/// Watches <c>&lt;projectPath&gt;/.firepit/inbox/*.md</c> and reports the
/// current unread count (top-level only — files moved to
/// <c>inbox/processed/</c> stop counting). Marshals nothing — consumers
/// hop dispatchers themselves if they touch UI.
/// </summary>
public sealed class InboxWatcher : IDisposable
{
    public const string InboxDirectory     = "inbox";
    public const string ProcessedDirectory = "processed";

    private readonly string _inboxPath;
    private readonly FileSystemWatcher? _fsw;
    private bool _disposed;

    public InboxWatcher(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ProjectPath = projectPath;
        _inboxPath  = Path.Combine(projectPath, ".firepit", InboxDirectory);

        try
        {
            Directory.CreateDirectory(_inboxPath);
            _fsw = new FileSystemWatcher(_inboxPath, "*.md")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
            };
            _fsw.Created += (_, _) => Refresh();
            _fsw.Deleted += (_, _) => Refresh();
            _fsw.Renamed += (_, _) => Refresh();
            _fsw.EnableRaisingEvents = true;
        }
        catch
        {
            // Filesystem hostile (path doesn't exist, permission denied) —
            // watcher stays null. Initial count check below still runs.
        }

        Refresh();
    }

    public string ProjectPath { get; }
    public string InboxPath   => _inboxPath;
    public int    UnreadCount { get; private set; }

    public event EventHandler<int>? UnreadCountChanged;

    public void Refresh()
    {
        try
        {
            var count = Directory.Exists(_inboxPath)
                ? Directory.EnumerateFiles(_inboxPath, "*.md", SearchOption.TopDirectoryOnly).Count()
                : 0;
            if (count == UnreadCount) return;
            UnreadCount = count;
            UnreadCountChanged?.Invoke(this, count);
        }
        catch
        {
            // Transient IO failure — leave count alone, retry on next event.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (_fsw is not null) { _fsw.EnableRaisingEvents = false; _fsw.Dispose(); } }
        catch { /* ignored */ }
    }
}
