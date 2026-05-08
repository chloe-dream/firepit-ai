namespace Firepit.Core.Time;

public sealed class SystemActivityClock : IActivityClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
