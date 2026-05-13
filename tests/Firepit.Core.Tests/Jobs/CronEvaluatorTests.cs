using Firepit.Core.Jobs;

namespace Firepit.Core.Tests.Jobs;

public class CronEvaluatorTests
{
    [Fact]
    public void TryParse_AcceptsStandard5FieldExpressions()
    {
        Assert.True(CronEvaluator.TryParse("*/30 * * * *", out _));
        Assert.True(CronEvaluator.TryParse("0 8 * * 1", out _));
        Assert.True(CronEvaluator.TryParse("0 9 * * 1-5", out _));
        Assert.True(CronEvaluator.TryParse("15 14 1 * *", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a cron")]
    [InlineData("99 * * * *")]
    [InlineData("* * * * * *")] // 6 fields — unsupported here
    public void TryParse_RejectsInvalidExpressions(string expr)
    {
        Assert.False(CronEvaluator.TryParse(expr, out _));
    }

    [Fact]
    public void NextOccurrence_RespectsTimezone()
    {
        Assert.True(CronEvaluator.TryParse("0 8 * * 1", out var schedule));
        var tz = TimeZoneInfo.FindSystemTimeZoneById("UTC");

        // Sunday 2026-05-10 00:00 UTC. Next Monday 08:00 UTC == 2026-05-11T08:00:00Z.
        var afterUtc = new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero);
        var next = CronEvaluator.NextOccurrence(schedule!, afterUtc, tz);

        Assert.NotNull(next);
        Assert.Equal(new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero), next!.Value);
    }

    [Fact]
    public void NextOccurrence_For30MinuteInterval_AdvancesHalfHour()
    {
        Assert.True(CronEvaluator.TryParse("*/30 * * * *", out var schedule));
        var tz = TimeZoneInfo.FindSystemTimeZoneById("UTC");

        var at = new DateTimeOffset(2026, 5, 13, 9, 5, 0, TimeSpan.Zero);
        var next = CronEvaluator.NextOccurrence(schedule!, at, tz);
        Assert.Equal(new DateTimeOffset(2026, 5, 13, 9, 30, 0, TimeSpan.Zero), next);

        var nextNext = CronEvaluator.NextOccurrence(schedule!, next!.Value, tz);
        Assert.Equal(new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero), nextNext);
    }

    [Fact]
    public void ResolveTimezone_NullOrEmpty_ReturnsLocal()
    {
        Assert.Equal(TimeZoneInfo.Local, CronEvaluator.ResolveTimezone(null));
        Assert.Equal(TimeZoneInfo.Local, CronEvaluator.ResolveTimezone(""));
        Assert.Equal(TimeZoneInfo.Local, CronEvaluator.ResolveTimezone("   "));
    }

    [Fact]
    public void ResolveTimezone_UnknownReturnsNull()
    {
        Assert.Null(CronEvaluator.ResolveTimezone("Mars/Olympus_Mons"));
    }
}
