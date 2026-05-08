namespace Firepit.Core.Time;

public interface IActivityClock
{
    DateTimeOffset UtcNow { get; }
}
