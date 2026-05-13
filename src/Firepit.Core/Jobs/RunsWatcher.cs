using System.IO;
using System.Text.Json;
using Firepit.Core.Settings;

namespace Firepit.Core.Jobs;

/// <summary>
/// Watches <c>&lt;projectPath&gt;/.firepit/runs/&lt;job&gt;/*.json</c> and
/// reports how many run records have arrived since the user last viewed the
/// runs folder. Mirrors <see cref="Firepit.Core.Inbox.InboxWatcher"/> — same
/// pattern, different source.
///
/// "Seen since" is persisted as one ISO8601 timestamp in
/// <c>&lt;projectPath&gt;/.firepit/runs/.seen</c>. Travels with the project
/// folder. Missing file = never seen → every record counts.
///
/// Policy filtering happens lazily: the timestamp in the filename is checked
/// first (cheap), and only candidates newer than <c>.seen</c> are deserialized
/// to apply <see cref="RunBadgePolicy.FailuresOnly"/>.
/// </summary>
public sealed class RunsWatcher : IDisposable
{
    public const string SeenFileName = ".seen";

    private readonly string _runsRoot;
    private readonly string _seenPath;
    private readonly RunBadgePolicy _policy;
    private readonly FileSystemWatcher? _fsw;
    private bool _disposed;

    public RunsWatcher(string projectPath, RunBadgePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ProjectPath = projectPath;
        _policy   = policy;
        _runsRoot = Path.Combine(projectPath, ".firepit", JsonJobHistoryStore.RunsDirectory);
        _seenPath = Path.Combine(_runsRoot, SeenFileName);

        try
        {
            Directory.CreateDirectory(_runsRoot);
            _fsw = new FileSystemWatcher(_runsRoot, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = true,
            };
            _fsw.Created += (_, _) => Refresh();
            _fsw.Changed += (_, _) => Refresh();
            _fsw.Deleted += (_, _) => Refresh();
            _fsw.Renamed += (_, _) => Refresh();
            _fsw.EnableRaisingEvents = true;
        }
        catch
        {
            // Filesystem hostile — leave watcher null; initial Refresh below
            // still reports whatever count we can read.
        }

        Refresh();
    }

    public string ProjectPath { get; }
    public string RunsPath    => _runsRoot;
    public int    UnreadCount { get; private set; }

    public event EventHandler<int>? UnreadCountChanged;

    /// <summary>
    /// Recompute <see cref="UnreadCount"/> by walking the runs tree and
    /// counting records newer than the persisted "seen" timestamp that match
    /// the configured <see cref="RunBadgePolicy"/>.
    /// </summary>
    public void Refresh()
    {
        try
        {
            var seen = ReadSeenTimestamp();
            var count = CountSince(seen);
            if (count == UnreadCount) return;
            UnreadCount = count;
            UnreadCountChanged?.Invoke(this, count);
        }
        catch
        {
            // Transient IO failure — keep current count; retry on next event.
        }
    }

    /// <summary>
    /// Mark all currently visible runs as seen — clears the badge. Writes the
    /// current UTC time to the sentinel file atomically (tmp + rename).
    /// </summary>
    public void MarkAllSeen()
    {
        try
        {
            Directory.CreateDirectory(_runsRoot);
            var tmp = _seenPath + ".tmp";
            File.WriteAllText(tmp, DateTimeOffset.UtcNow.ToString("O"));
            File.Move(tmp, _seenPath, overwrite: true);
        }
        catch
        {
            // Best-effort. If we can't persist, the count just won't drop
            // until the user can write to the project dir.
        }
        Refresh();
    }

    private DateTimeOffset ReadSeenTimestamp()
    {
        if (!File.Exists(_seenPath)) return DateTimeOffset.MinValue;
        try
        {
            var raw = File.ReadAllText(_seenPath).Trim();
            return DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed) ? parsed : DateTimeOffset.MinValue;
        }
        catch
        {
            return DateTimeOffset.MinValue;
        }
    }

    private int CountSince(DateTimeOffset seen)
    {
        if (!Directory.Exists(_runsRoot)) return 0;

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(_runsRoot, "*.json", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            var ts = JsonJobHistoryStore.TryParseFileName(name);
            if (ts is null || ts <= seen) continue;

            if (_policy == RunBadgePolicy.All)
            {
                count++;
                continue;
            }

            // FailuresOnly: deserialize to check status. Skipped/Success are
            // hidden; Failure / Timeout / Interrupted count.
            if (TryReadStatus(file) is { } status && IsFailure(status))
            {
                count++;
            }
        }
        return count;
    }

    private static JobRunStatus? TryReadStatus(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var record = JsonSerializer.Deserialize(stream, JobRunJsonContext.Default.JobRunRecord);
            return record?.Status;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsFailure(JobRunStatus status) =>
        status is JobRunStatus.Failure or JobRunStatus.Timeout or JobRunStatus.Interrupted;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (_fsw is not null) { _fsw.EnableRaisingEvents = false; _fsw.Dispose(); } }
        catch { /* ignored */ }
    }
}
