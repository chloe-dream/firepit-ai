namespace Firepit.Core.Jobs;

/// <summary>
/// Scheduler tunables. Production wiring uses <see cref="Defaults"/>; tests
/// use a short <see cref="TickInterval"/> so the driver loop fires often.
/// </summary>
public sealed record JobSchedulerOptions(
    TimeSpan TickInterval,
    int MaxQueueDepth)
{
    public static readonly JobSchedulerOptions Defaults = new(
        TickInterval: TimeSpan.FromSeconds(30),
        MaxQueueDepth: 1);
}
