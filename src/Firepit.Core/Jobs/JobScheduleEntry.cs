using Firepit.Core.ProjectConfig;

namespace Firepit.Core.Jobs;

/// <summary>
/// One scheduled job, materialised for the scheduler — the original
/// <see cref="ProjectScheduledJob"/> plus the project context it belongs to
/// and a resolved timezone. Built by an <see cref="IJobScheduleSource"/>.
/// </summary>
public sealed record JobScheduleEntry(
    string ProjectPath,
    string ProjectName,
    ProjectScheduledJob Job,
    TimeZoneInfo Timezone);

/// <summary>
/// Provides the current set of scheduled jobs Firepit should be running.
/// The file-system implementation scans the projects root for
/// <c>.firepit/config.json</c> files; tests can supply a static list.
/// </summary>
public interface IJobScheduleSource
{
    /// <summary>
    /// Snapshot of all known schedules. Idempotent — callers may invoke this
    /// repeatedly (e.g. after a config hot-reload) and should diff against
    /// their last view to detect adds / removes / changes.
    /// </summary>
    IReadOnlyList<JobScheduleEntry> Enumerate();
}
