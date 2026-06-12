using System.IO;
using Firepit.Core.Jobs;

namespace Firepit.Core.Tests.Jobs;

public class JsonJobHistoryStoreTests : IDisposable
{
    private readonly string _projectPath;

    public JsonJobHistoryStoreTests()
    {
        _projectPath = Path.Combine(Path.GetTempPath(), "firepit-history-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_projectPath, recursive: true); } catch { /* best effort */ }
    }

    private static JobRunOutcome MakeOutcome(
        JobRunStatus status = JobRunStatus.Success,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null) =>
        new(
            Status: status,
            ExitCode: status == JobRunStatus.Success ? 0 : 1,
            StartedAt: startedAt ?? new DateTimeOffset(2026, 5, 13, 8, 30, 0, TimeSpan.Zero),
            EndedAt: endedAt ?? new DateTimeOffset(2026, 5, 13, 8, 30, 14, TimeSpan.Zero),
            CommandLine: "claude -p /check-mails --output-format json",
            StdoutInline: """{"result":"ok"}""",
            StdoutTruncated: false,
            StdoutSpilloverPath: null,
            Stderr: "",
            ClaudeMetadata: new ClaudeResultMetadata(10, 5, 0.01m, null, "ok"));

    [Fact]
    public async Task RecordAsync_WritesFileWithSortableTimestampName()
    {
        var store = new JsonJobHistoryStore(retention: TimeSpan.FromDays(3650));
        await store.RecordAsync(_projectPath, "demo", "check-mails", "/check-mails", JobTrigger.Scheduled,
            MakeOutcome(), CancellationToken.None);

        var dir = Path.Combine(_projectPath, ".firepit", "runs", "check-mails");
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Single(files);
        Assert.EndsWith("2026-05-13T08-30-00-000Z.json", files[0]);
    }

    [Fact]
    public async Task RecordedFile_RoundtripsAllFields()
    {
        var store = new JsonJobHistoryStore(retention: TimeSpan.FromDays(3650));
        await store.RecordAsync(_projectPath, "demo", "check-mails", "/check-mails", JobTrigger.Manual,
            MakeOutcome(), CancellationToken.None);

        var records = store.Load(_projectPath, "check-mails");
        var rec = Assert.Single(records);

        Assert.Equal("check-mails", rec.JobName);
        Assert.Equal("demo", rec.ProjectName);
        Assert.Equal("/check-mails", rec.Prompt);
        Assert.Equal(JobTrigger.Manual, rec.Trigger);
        Assert.Equal(JobRunStatus.Success, rec.Status);
        Assert.Equal(0, rec.ExitCode);
        Assert.Equal(10, rec.TokensInput);
        Assert.Equal(0.01m, rec.CostUsd);
        Assert.Equal("ok", rec.AssistantMessage);
    }

    [Fact]
    public async Task GetLastRunStartedAt_ReturnsMostRecentTimestamp()
    {
        var store = new JsonJobHistoryStore(retention: TimeSpan.FromDays(3650));
        var older  = new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero);
        var newer  = new DateTimeOffset(2026, 5, 13, 9, 0, 0, TimeSpan.Zero);

        await store.RecordAsync(_projectPath, "demo", "check-mails", "/check-mails", JobTrigger.Scheduled,
            MakeOutcome(startedAt: older, endedAt: older.AddSeconds(5)), CancellationToken.None);
        await store.RecordAsync(_projectPath, "demo", "check-mails", "/check-mails", JobTrigger.Scheduled,
            MakeOutcome(startedAt: newer, endedAt: newer.AddSeconds(5)), CancellationToken.None);

        var last = store.GetLastRunStartedAt(_projectPath, "check-mails");
        Assert.Equal(newer, last);
    }

    [Fact]
    public async Task RecoverInterrupted_MarksRecordsWithoutEndedAt()
    {
        // Hand-craft a record with EndedAt = null
        var dir = Path.Combine(_projectPath, ".firepit", "runs", "check-mails");
        Directory.CreateDirectory(dir);
        var brokenPath = Path.Combine(dir, "2026-05-13T08-00-00Z.json");
        var brokenRecord = """
        {
          "version": 1,
          "jobName": "check-mails",
          "projectName": "demo",
          "trigger": "scheduled",
          "status": "success",
          "startedAt": "2026-05-13T08:00:00+00:00",
          "endedAt": null,
          "prompt": "",
          "commandLine": "claude -p /check-mails",
          "stdoutInline": "",
          "stdoutTruncated": false,
          "stderr": ""
        }
        """;
        await File.WriteAllTextAsync(brokenPath, brokenRecord);

        var store = new JsonJobHistoryStore(retention: TimeSpan.FromDays(3650));
        await store.RecoverInterruptedAsync(_projectPath, CancellationToken.None);

        var reloaded = store.Load(_projectPath, "check-mails");
        var rec = Assert.Single(reloaded);
        Assert.Equal(JobRunStatus.Interrupted, rec.Status);
        Assert.NotNull(rec.EndedAt);
        Assert.NotNull(rec.DurationMs);
    }

    [Fact]
    public async Task Retention_DeletesRecordsOlderThanCutoff()
    {
        var store = new JsonJobHistoryStore(retention: TimeSpan.FromDays(7));

        // Drop a placeholder file with a 60-days-ago filename — retention
        // looks only at the filename timestamp, not the body.
        var dir = Path.Combine(_projectPath, ".firepit", "runs", "check-mails");
        Directory.CreateDirectory(dir);
        var ancientName = JsonJobHistoryStore.FormatFileName(DateTimeOffset.UtcNow.AddDays(-60));
        var ancientPath = Path.Combine(dir, ancientName);
        await File.WriteAllTextAsync(ancientPath, "{}");

        // RecordAsync triggers retention pass on every write. The fresh record
        // must carry a *now* timestamp — files are named after StartedAt, so a
        // hardcoded fixture date eventually drifts past the 7-day cutoff and
        // the "surviving" record gets swept up too (this test was a time-bomb
        // that started failing 7 days after the fixture's 2026-05-13 date).
        var now = DateTimeOffset.UtcNow;
        await store.RecordAsync(_projectPath, "demo", "check-mails", "/check-mails", JobTrigger.Scheduled,
            MakeOutcome(startedAt: now, endedAt: now.AddSeconds(14)), CancellationToken.None);

        Assert.False(File.Exists(ancientPath), "expected ancient record to be deleted by retention");
        Assert.Single(Directory.GetFiles(dir, "*.json"));
    }

    [Fact]
    public void SanitizeJobName_ReplacesInvalidChars()
    {
        Assert.Equal("ok-name", JsonJobHistoryStore.SanitizeJobName("ok-name"));
        Assert.Equal("bad_name", JsonJobHistoryStore.SanitizeJobName("bad/name"));
        Assert.Equal("with_colon", JsonJobHistoryStore.SanitizeJobName("with:colon"));
    }

    [Fact]
    public void FileNameRoundtrip_MillisecondPrecision()
    {
        var ts = new DateTimeOffset(2026, 5, 13, 8, 30, 0, 123, TimeSpan.Zero);
        var name = JsonJobHistoryStore.FormatFileName(ts);
        Assert.Equal("2026-05-13T08-30-00-123Z.json", name);
        Assert.Equal(ts, JsonJobHistoryStore.TryParseFileName(name));
    }

    [Fact]
    public void FileNameRoundtrip_LegacySecondPrecisionStillParses()
    {
        var legacyName = "2026-05-13T08-30-00Z.json";
        var parsed = JsonJobHistoryStore.TryParseFileName(legacyName);
        Assert.Equal(new DateTimeOffset(2026, 5, 13, 8, 30, 0, TimeSpan.Zero), parsed);
    }

    [Fact]
    public async Task SameSecondRuns_DoNotCollide()
    {
        var store = new JsonJobHistoryStore(retention: TimeSpan.FromDays(3650));
        var ts = new DateTimeOffset(2026, 5, 13, 8, 30, 0, TimeSpan.Zero);

        await store.RecordAsync(_projectPath, "demo", "check-mails", "/check-mails", JobTrigger.Scheduled,
            MakeOutcome(startedAt: ts.AddMilliseconds(100), endedAt: ts.AddSeconds(1)),
            CancellationToken.None);
        await store.RecordAsync(_projectPath, "demo", "check-mails", "/check-mails", JobTrigger.Manual,
            MakeOutcome(startedAt: ts.AddMilliseconds(750), endedAt: ts.AddSeconds(1).AddMilliseconds(750)),
            CancellationToken.None);

        var dir = Path.Combine(_projectPath, ".firepit", "runs", "check-mails");
        Assert.Equal(2, Directory.GetFiles(dir, "*.json").Length);
    }
}
