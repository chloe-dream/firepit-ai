namespace Firepit.Core.Jobs;

/// <summary>
/// Spawns a single headless Claude run as a non-PTY child process.
/// Implementations live outside Core (e.g. <c>Firepit.Process.ProcessJobRunner</c>);
/// the seam lets the scheduler be unit-tested with a fake runner.
/// </summary>
public interface IJobRunner
{
    Task<JobRunOutcome> RunAsync(JobRunRequest request, CancellationToken ct);
}
