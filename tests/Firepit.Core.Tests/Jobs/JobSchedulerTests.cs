using System.Collections.Concurrent;
using Firepit.Core.Jobs;
using Firepit.Core.ProjectConfig;
using Firepit.Core.Time;

namespace Firepit.Core.Tests.Jobs;

public class JobSchedulerTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.FindSystemTimeZoneById("UTC");

    private sealed class FakeClock : IActivityClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 5, 13, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeRunner : IJobRunner
    {
        public readonly ConcurrentBag<(string Job, JobTrigger Trigger, DateTimeOffset StartedAt)> Invocations = new();
        public TaskCompletionSource? Gate;
#pragma warning disable CS0649 // assigned via object initializer in tests
        public Func<JobRunRequest, JobRunOutcome>? OutcomeFactory;
#pragma warning restore CS0649

        public async Task<JobRunOutcome> RunAsync(JobRunRequest request, CancellationToken ct)
        {
            var startedAt = DateTimeOffset.UtcNow;
            Invocations.Add((request.JobName, request.Trigger, startedAt));
            if (Gate is not null) await Gate.Task.WaitAsync(ct).ConfigureAwait(false);
            var outcome = OutcomeFactory?.Invoke(request) ?? new JobRunOutcome(
                JobRunStatus.Success, 0, startedAt, DateTimeOffset.UtcNow,
                "fake", "", false, null, "");
            return outcome;
        }
    }

    private sealed class FakeHistory : IJobHistoryStore
    {
        public readonly ConcurrentBag<(string Job, JobTrigger Trigger, JobRunStatus Status)> Records = new();
        public readonly ConcurrentDictionary<string, DateTimeOffset> LastRun = new();
        public int RecoverCalls;

        public Task RecordAsync(string projectPath, string projectName, string jobName,
            JobTrigger trigger, JobRunOutcome outcome, CancellationToken ct)
        {
            Records.Add((jobName, trigger, outcome.Status));
            LastRun[$"{projectPath}||{jobName}"] = outcome.StartedAt;
            return Task.CompletedTask;
        }

        public DateTimeOffset? GetLastRunStartedAt(string projectPath, string jobName) =>
            LastRun.TryGetValue($"{projectPath}||{jobName}", out var v) ? v : null;

        public Task RecoverInterruptedAsync(string projectPath, CancellationToken ct)
        {
            Interlocked.Increment(ref RecoverCalls);
            return Task.CompletedTask;
        }
    }

    private sealed class StaticSource : IJobScheduleSource
    {
        public List<JobScheduleEntry> Entries { get; } = new();
        public IReadOnlyList<JobScheduleEntry> Enumerate() => Entries;
    }

    private static JobScheduleEntry Entry(string jobName, string cron,
        JobConcurrencyPolicy? policy = null) =>
        new(
            ProjectPath: @"C:\projects\demo",
            ProjectName: "demo",
            Job: new ProjectScheduledJob(
                Name: jobName,
                Prompt: $"/{jobName}",
                Schedule: cron,
                OnConcurrent: policy),
            Timezone: Utc);

    [Fact]
    public async Task DueJob_FiresOnTick()
    {
        // Scheduler starts at 09:00; cron slot at 09:30 passes; tick at 09:30:30.
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 5, 13, 9, 0, 0, TimeSpan.Zero) };
        var runner = new FakeRunner();
        var history = new FakeHistory();
        var source = new StaticSource { Entries = { Entry("check-mails", "*/30 * * * *") } };

        await using var sched = new JobScheduler(source, runner, history, clock,
            JobSchedulerOptions.Defaults with { TickInterval = TimeSpan.FromMinutes(1) });

        clock.UtcNow = new DateTimeOffset(2026, 5, 13, 9, 30, 30, TimeSpan.Zero);
        await sched.TickOnceAsync(CancellationToken.None);
        await Task.Delay(50);

        Assert.Single(runner.Invocations);
        var inv = runner.Invocations.First();
        Assert.Equal("check-mails", inv.Job);
        Assert.Equal(JobTrigger.Scheduled, inv.Trigger);
    }

    [Fact]
    public async Task NotDueYet_DoesNotFire()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 5, 13, 9, 5, 0, TimeSpan.Zero) };
        var runner = new FakeRunner();
        var history = new FakeHistory();
        var source = new StaticSource { Entries = { Entry("check-mails", "*/30 * * * *") } };

        await using var sched = new JobScheduler(source, runner, history, clock);
        await sched.TickOnceAsync(CancellationToken.None);
        await Task.Delay(30);

        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task SecondTickAtSameSlot_DoesNotRefire()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 5, 13, 9, 0, 0, TimeSpan.Zero) };
        var runner = new FakeRunner();
        var history = new FakeHistory();
        var source = new StaticSource { Entries = { Entry("check-mails", "*/30 * * * *") } };

        await using var sched = new JobScheduler(source, runner, history, clock);
        clock.UtcNow = new DateTimeOffset(2026, 5, 13, 9, 30, 30, TimeSpan.Zero);
        await sched.TickOnceAsync(CancellationToken.None);
        await Task.Delay(30);
        await sched.TickOnceAsync(CancellationToken.None);
        await Task.Delay(30);

        Assert.Single(runner.Invocations);
    }

    [Fact]
    public async Task ConcurrencySkip_RecordsSkippedWhenStillRunning()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 5, 13, 9, 0, 0, TimeSpan.Zero) };
        var runner = new FakeRunner { Gate = new TaskCompletionSource() };
        var history = new FakeHistory();
        var source = new StaticSource
        {
            Entries = { Entry("slow", "*/30 * * * *", JobConcurrencyPolicy.Skip) },
        };

        await using var sched = new JobScheduler(source, runner, history, clock);

        clock.UtcNow = new DateTimeOffset(2026, 5, 13, 9, 30, 30, TimeSpan.Zero);
        await sched.TickOnceAsync(CancellationToken.None);
        await Task.Delay(30); // let runner enter Gate

        clock.UtcNow = new DateTimeOffset(2026, 5, 13, 10, 0, 30, TimeSpan.Zero);
        await sched.TickOnceAsync(CancellationToken.None);

        runner.Gate!.SetResult();
        await Task.Delay(50);

        Assert.Contains(history.Records, r => r.Status == JobRunStatus.Skipped);
        Assert.Single(runner.Invocations); // only the first actually ran
    }

    [Fact]
    public async Task ConcurrencyQueue_DefersRunInsteadOfSkipping()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 5, 13, 9, 0, 0, TimeSpan.Zero) };
        var runner = new FakeRunner { Gate = new TaskCompletionSource() };
        var history = new FakeHistory();
        var source = new StaticSource
        {
            Entries = { Entry("slow", "*/30 * * * *", JobConcurrencyPolicy.Queue) },
        };

        await using var sched = new JobScheduler(source, runner, history, clock);

        clock.UtcNow = new DateTimeOffset(2026, 5, 13, 9, 30, 30, TimeSpan.Zero);
        await sched.TickOnceAsync(CancellationToken.None);
        await Task.Delay(30);

        clock.UtcNow = new DateTimeOffset(2026, 5, 13, 10, 0, 30, TimeSpan.Zero);
        await sched.TickOnceAsync(CancellationToken.None);

        // No skip yet — it should be queued.
        Assert.DoesNotContain(history.Records, r => r.Status == JobRunStatus.Skipped);

        // Let the first run complete; second should fire automatically.
        runner.Gate!.SetResult();
        await Task.Delay(150);

        Assert.Equal(2, runner.Invocations.Count);
    }

    [Fact]
    public async Task ManualTrigger_FiresImmediately()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 5, 13, 9, 5, 0, TimeSpan.Zero) };
        var runner = new FakeRunner();
        var history = new FakeHistory();
        var source = new StaticSource { Entries = { Entry("check-mails", "0 0 1 1 *") } };

        await using var sched = new JobScheduler(source, runner, history, clock);
        await sched.TriggerNowAsync(@"C:\projects\demo", "check-mails", CancellationToken.None);
        await Task.Delay(40);

        Assert.Single(runner.Invocations);
        Assert.Equal(JobTrigger.Manual, runner.Invocations.First().Trigger);
    }

    [Fact]
    public async Task Catchup_FiresOnceWhenLastRunIsTooOld()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 5, 13, 10, 5, 0, TimeSpan.Zero) };
        var runner = new FakeRunner();
        var history = new FakeHistory();

        // Last run at 09:00 — schedule fires every 30 min, so 09:30 and 10:00 were missed.
        history.LastRun[@"C:\projects\demo||check-mails"] =
            new DateTimeOffset(2026, 5, 13, 9, 0, 0, TimeSpan.Zero);

        var source = new StaticSource { Entries = { Entry("check-mails", "*/30 * * * *") } };

        await using var sched = new JobScheduler(source, runner, history, clock);
        await sched.StartAsync(CancellationToken.None);
        await Task.Delay(80);

        var catchups = runner.Invocations.Count(i => i.Trigger == JobTrigger.Catchup);
        Assert.Equal(1, catchups); // exactly one catch-up, not "all missed slots"
    }

    [Fact]
    public async Task ParallelManualTriggers_OnlyOneRuns()
    {
        // Two TriggerNowAsync calls fired concurrently must not produce two
        // overlapping runs — the second sees RunningTask alive and yields to
        // concurrency policy (default Skip).
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 5, 13, 9, 0, 0, TimeSpan.Zero) };
        var runner = new FakeRunner { Gate = new TaskCompletionSource() };
        var history = new FakeHistory();
        var source = new StaticSource { Entries = { Entry("slow", "0 0 1 1 *") } };

        await using var sched = new JobScheduler(source, runner, history, clock);

        var t1 = sched.TriggerNowAsync(@"C:\projects\demo", "slow", CancellationToken.None);
        var t2 = sched.TriggerNowAsync(@"C:\projects\demo", "slow", CancellationToken.None);
        await Task.WhenAll(t1, t2);
        await Task.Delay(50);

        Assert.Single(runner.Invocations);
        runner.Gate!.SetResult();
        await Task.Delay(50);
    }

    [Fact]
    public async Task Start_InvokesInterruptedRecoveryOncePerProject()
    {
        var clock = new FakeClock();
        var runner = new FakeRunner();
        var history = new FakeHistory();
        var source = new StaticSource
        {
            Entries =
            {
                Entry("a", "0 0 1 1 *"),
                Entry("b", "0 0 1 1 *"), // same project as 'a' — should recover once
            },
        };

        await using var sched = new JobScheduler(source, runner, history, clock);
        await sched.StartAsync(CancellationToken.None);

        Assert.Equal(1, history.RecoverCalls);
    }
}
