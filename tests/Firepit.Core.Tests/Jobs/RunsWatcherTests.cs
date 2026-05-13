using System.IO;
using Firepit.Core.Jobs;
using Firepit.Core.Settings;

namespace Firepit.Core.Tests.Jobs;

public class RunsWatcherTests : IDisposable
{
    private readonly string _projectDir;

    public RunsWatcherTests()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "firepit-runs-watcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_projectDir, recursive: true); } catch { /* best effort */ }
    }

    private async Task WriteRecord(string jobName, DateTimeOffset startedAt,
        JobRunStatus status = JobRunStatus.Success)
    {
        var store = new JsonJobHistoryStore();
        var outcome = new JobRunOutcome(
            Status: status,
            ExitCode: status == JobRunStatus.Success ? 0 : 1,
            StartedAt: startedAt,
            EndedAt: startedAt.AddSeconds(1),
            CommandLine: "claude -p /test",
            StdoutInline: "",
            StdoutTruncated: false,
            StdoutSpilloverPath: null,
            Stderr: "");
        await store.RecordAsync(_projectDir, "demo", jobName, "/test",
            JobTrigger.Scheduled, outcome, CancellationToken.None);
    }

    [Fact]
    public void Initial_CountIsZero_WhenRunsEmpty()
    {
        using var watcher = new RunsWatcher(_projectDir, RunBadgePolicy.All);
        Assert.Equal(0, watcher.UnreadCount);
    }

    [Fact]
    public async Task Counts_AllRunsWhenNeverSeen_WithPolicyAll()
    {
        await WriteRecord("check-mails", DateTimeOffset.UtcNow.AddMinutes(-10));
        await WriteRecord("check-mails", DateTimeOffset.UtcNow.AddMinutes(-5), JobRunStatus.Failure);
        await WriteRecord("audit",       DateTimeOffset.UtcNow.AddMinutes(-1));

        using var watcher = new RunsWatcher(_projectDir, RunBadgePolicy.All);
        Assert.Equal(3, watcher.UnreadCount);
    }

    [Fact]
    public async Task FailuresOnly_HidesSuccessAndSkipped()
    {
        await WriteRecord("check-mails", DateTimeOffset.UtcNow.AddMinutes(-10), JobRunStatus.Success);
        await WriteRecord("check-mails", DateTimeOffset.UtcNow.AddMinutes(-5),  JobRunStatus.Failure);
        await WriteRecord("audit",       DateTimeOffset.UtcNow.AddMinutes(-3),  JobRunStatus.Timeout);
        await WriteRecord("audit",       DateTimeOffset.UtcNow.AddMinutes(-2),  JobRunStatus.Skipped);

        using var watcher = new RunsWatcher(_projectDir, RunBadgePolicy.FailuresOnly);
        // Failure + Timeout = 2; Success and Skipped don't surface.
        Assert.Equal(2, watcher.UnreadCount);
    }

    [Fact]
    public async Task MarkAllSeen_ClearsTheCount()
    {
        await WriteRecord("check-mails", DateTimeOffset.UtcNow.AddMinutes(-1));

        using var watcher = new RunsWatcher(_projectDir, RunBadgePolicy.All);
        Assert.Equal(1, watcher.UnreadCount);

        watcher.MarkAllSeen();
        Assert.Equal(0, watcher.UnreadCount);
    }

    [Fact]
    public async Task NewRunAfterSeen_CountsAgain()
    {
        // First record then mark-seen → count 0.
        await WriteRecord("check-mails", DateTimeOffset.UtcNow.AddMinutes(-10));
        using var watcher = new RunsWatcher(_projectDir, RunBadgePolicy.All);
        watcher.MarkAllSeen();
        Assert.Equal(0, watcher.UnreadCount);

        // Second record, newer than .seen → manual refresh picks it up.
        await WriteRecord("check-mails", DateTimeOffset.UtcNow.AddSeconds(5));
        watcher.Refresh();
        Assert.Equal(1, watcher.UnreadCount);
    }

    [Fact]
    public async Task SeenFile_LivesUnderRunsDir()
    {
        await WriteRecord("check-mails", DateTimeOffset.UtcNow.AddMinutes(-1));
        using var watcher = new RunsWatcher(_projectDir, RunBadgePolicy.All);
        watcher.MarkAllSeen();

        var seen = Path.Combine(_projectDir, ".firepit", "runs", RunsWatcher.SeenFileName);
        Assert.True(File.Exists(seen), $"expected {seen} after MarkAllSeen()");
    }
}
