using NCrontab;

namespace Firepit.Core.Jobs;

/// <summary>
/// Pure wrapper around <see cref="CrontabSchedule"/> with timezone-aware
/// occurrence math. Standard 5-field cron expressions
/// (<c>minute hour day-of-month month day-of-week</c>) — no seconds field.
///
/// All wall-clock arithmetic happens in the supplied timezone (default: local);
/// only the returned <see cref="DateTimeOffset"/> values are in UTC. This keeps
/// "every Monday at 08:00 Europe/Berlin" semantics intact across DST transitions.
/// </summary>
public static class CronEvaluator
{
    public static bool TryParse(string expression, out CrontabSchedule? schedule)
    {
        schedule = null;
        if (string.IsNullOrWhiteSpace(expression)) return false;
        try
        {
            schedule = CrontabSchedule.Parse(expression);
            return true;
        }
        catch (CrontabException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the next firing strictly after <paramref name="afterUtc"/>, or
    /// <c>null</c> if the expression yields nothing in a reasonable window
    /// (NCrontab caps internally).
    /// </summary>
    public static DateTimeOffset? NextOccurrence(
        CrontabSchedule schedule,
        DateTimeOffset afterUtc,
        TimeZoneInfo? timezone = null)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var tz = timezone ?? TimeZoneInfo.Local;

        var afterLocal = TimeZoneInfo.ConvertTimeFromUtc(afterUtc.UtcDateTime, tz);
        var nextLocal  = schedule.GetNextOccurrence(afterLocal);
        if (nextLocal == DateTime.MinValue || nextLocal <= afterLocal) return null;

        try
        {
            var nextUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified), tz);
            return new DateTimeOffset(nextUtc, TimeSpan.Zero);
        }
        catch (ArgumentException)
        {
            // Skipped local time (spring-forward DST). NCrontab returned a local
            // time that does not exist in this zone. Try one minute later.
            return NextOccurrence(schedule, new DateTimeOffset(
                TimeZoneInfo.ConvertTimeToUtc(nextLocal.AddMinutes(1), tz), TimeSpan.Zero), tz);
        }
    }

    /// <summary>
    /// Resolves an IANA or Windows timezone string. Returns <see cref="TimeZoneInfo.Local"/>
    /// when the value is null/empty; returns <c>null</c> when the string is set
    /// but unknown — callers should treat that as a config error.
    /// </summary>
    public static TimeZoneInfo? ResolveTimezone(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(name); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException)  { return null; }
    }
}
