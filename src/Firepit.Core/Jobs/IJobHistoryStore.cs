namespace Firepit.Core.Jobs;

/// <summary>
/// Where the scheduler persists run outcomes and reads the most recent run
/// for catch-up decisions. Phase 4 ships the file-system implementation
/// under <c>&lt;projectPath&gt;/.firepit/runs/</c>; the seam lets the
/// scheduler be tested with an in-memory fake.
/// </summary>
public interface IJobHistoryStore
{
    /// <summary>
    /// Persist a completed run record for the given project/job. The
    /// <paramref name="trigger"/> distinguishes scheduled / manual / catchup
    /// in the saved record. <paramref name="prompt"/> is what was sent to
    /// the agent — the runner doesn't carry it back, so the scheduler passes
    /// it through.
    /// </summary>
    Task RecordAsync(
        string projectPath,
        string projectName,
        string jobName,
        string prompt,
        JobTrigger trigger,
        JobRunOutcome outcome,
        CancellationToken ct);

    /// <summary>
    /// Latest <c>startedAt</c> for the named job in the project, or
    /// <c>null</c> if there is no prior run on disk. Used by catch-up
    /// (Phase 3) and the badge "seen since" math (Phase 5).
    /// </summary>
    DateTimeOffset? GetLastRunStartedAt(string projectPath, string jobName);

    /// <summary>
    /// Mark any run records where <c>endedAt</c> is missing as
    /// <c>Interrupted</c>. Called once at scheduler startup to recover from
    /// a crash or forced exit during a previous run.
    /// </summary>
    Task RecoverInterruptedAsync(string projectPath, CancellationToken ct);
}
