namespace Firepit.Core.Sessions;

public sealed record ActivitySettings(
    int IdleThresholdMs,
    int IgnitingTimeoutMs)
{
    public static readonly ActivitySettings Default = new(
        IdleThresholdMs: 1500,
        IgnitingTimeoutMs: 10_000);
}
