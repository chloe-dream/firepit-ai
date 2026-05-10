using System.IO;
using Firepit.Core.Inbox;

namespace Firepit.Core.Tests.Inbox;

public class InboxWatcherTests : IDisposable
{
    private readonly string _projectDir;

    public InboxWatcherTests()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "firepit-inbox-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_projectDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Initial_CountIsZero_WhenInboxEmpty()
    {
        using var watcher = new InboxWatcher(_projectDir);
        Assert.Equal(0, watcher.UnreadCount);
    }

    [Fact]
    public void Initial_CountsExistingMarkdown()
    {
        var inbox = Path.Combine(_projectDir, ".firepit", "inbox");
        Directory.CreateDirectory(inbox);
        File.WriteAllText(Path.Combine(inbox, "a.md"), "x");
        File.WriteAllText(Path.Combine(inbox, "b.md"), "y");

        using var watcher = new InboxWatcher(_projectDir);
        Assert.Equal(2, watcher.UnreadCount);
    }

    [Fact]
    public void Refresh_IgnoresProcessedSubfolder()
    {
        var inbox = Path.Combine(_projectDir, ".firepit", "inbox");
        var processed = Path.Combine(inbox, "processed");
        Directory.CreateDirectory(processed);
        File.WriteAllText(Path.Combine(inbox, "live.md"), "x");
        File.WriteAllText(Path.Combine(processed, "old.md"), "y");

        using var watcher = new InboxWatcher(_projectDir);
        Assert.Equal(1, watcher.UnreadCount);
    }

    [Fact]
    public void Refresh_OnlyMarkdownFiles()
    {
        var inbox = Path.Combine(_projectDir, ".firepit", "inbox");
        Directory.CreateDirectory(inbox);
        File.WriteAllText(Path.Combine(inbox, "msg.md"), "x");
        File.WriteAllText(Path.Combine(inbox, "data.json"), "{}");
        File.WriteAllText(Path.Combine(inbox, "readme.txt"), "");

        using var watcher = new InboxWatcher(_projectDir);
        Assert.Equal(1, watcher.UnreadCount);
    }

    [Fact]
    public void InboxPath_PointsAtFirepitInboxDir()
    {
        using var watcher = new InboxWatcher(_projectDir);
        Assert.Equal(Path.Combine(_projectDir, ".firepit", "inbox"), watcher.InboxPath);
    }

    [Fact]
    public void Refresh_FiresEventOnDelta()
    {
        var inbox = Path.Combine(_projectDir, ".firepit", "inbox");
        Directory.CreateDirectory(inbox);
        using var watcher = new InboxWatcher(_projectDir);

        var fired = 0;
        var lastCount = -1;
        watcher.UnreadCountChanged += (_, c) => { fired++; lastCount = c; };

        File.WriteAllText(Path.Combine(inbox, "fresh.md"), "x");
        watcher.Refresh();

        Assert.Equal(1, fired);
        Assert.Equal(1, lastCount);
        Assert.Equal(1, watcher.UnreadCount);
    }

    [Fact]
    public void Refresh_NoEventWhenCountUnchanged()
    {
        var inbox = Path.Combine(_projectDir, ".firepit", "inbox");
        Directory.CreateDirectory(inbox);
        using var watcher = new InboxWatcher(_projectDir);

        var fired = 0;
        watcher.UnreadCountChanged += (_, _) => fired++;

        watcher.Refresh();
        watcher.Refresh();
        watcher.Refresh();

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Constructor_CreatesInboxDirectoryIfMissing()
    {
        Assert.False(Directory.Exists(Path.Combine(_projectDir, ".firepit", "inbox")));

        using var watcher = new InboxWatcher(_projectDir);

        Assert.True(Directory.Exists(Path.Combine(_projectDir, ".firepit", "inbox")));
    }
}
