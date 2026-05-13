using System.Text.Json.Serialization;

namespace Firepit.Core.Jobs;

/// <summary>
/// Terminal status of a job run.
/// <list type="bullet">
///   <item><c>Success</c> — child exited with code 0</item>
///   <item><c>Failure</c> — child exited with non-zero code</item>
///   <item><c>Timeout</c> — Firepit killed the child after the configured timeout</item>
///   <item><c>Skipped</c> — scheduler suppressed this tick because a prior run is still active
///         (concurrency policy <c>skip</c>); no process was spawned</item>
///   <item><c>Interrupted</c> — Firepit was closed (or crashed) while the run was active;
///         status is set on next launch when a record without <c>endedAt</c> is found</item>
/// </list>
/// </summary>
public enum JobRunStatus
{
    [JsonStringEnumMemberName("success")]     Success,
    [JsonStringEnumMemberName("failure")]     Failure,
    [JsonStringEnumMemberName("timeout")]     Timeout,
    [JsonStringEnumMemberName("skipped")]     Skipped,
    [JsonStringEnumMemberName("interrupted")] Interrupted,
}
