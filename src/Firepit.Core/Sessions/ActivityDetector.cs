using Firepit.Core.Time;

namespace Firepit.Core.Sessions;

public sealed class ActivityDetector
{
    private readonly IActivityClock _clock;
    private readonly TimeSpan _idleThreshold;
    private readonly TimeSpan _ignitingTimeout;
    private readonly object _gate = new();

    private SessionState _state = SessionState.Cold;
    private DateTimeOffset? _lastReadAt;
    private DateTimeOffset? _ignitedAt;

    public ActivityDetector(IActivityClock clock, ActivitySettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        var resolved = settings ?? ActivitySettings.Default;
        _idleThreshold = TimeSpan.FromMilliseconds(resolved.IdleThresholdMs);
        _ignitingTimeout = TimeSpan.FromMilliseconds(resolved.IgnitingTimeoutMs);
    }

    public SessionState State
    {
        get { lock (_gate) { return _state; } }
    }

    public event EventHandler<SessionState>? StateChanged;

    public void NotifyIgniting()
    {
        SessionState? transition;
        lock (_gate)
        {
            _ignitedAt = _clock.UtcNow;
            _lastReadAt = null;
            transition = SetLocked(SessionState.Igniting);
        }
        if (transition is { } s) StateChanged?.Invoke(this, s);
    }

    public void NotifyRead()
    {
        SessionState? transition = null;
        lock (_gate)
        {
            _lastReadAt = _clock.UtcNow;
            if (_state == SessionState.Igniting || _state == SessionState.Embers)
            {
                transition = SetLocked(SessionState.Burning);
            }
            else if (_state == SessionState.Cold)
            {
                // First read with no prior NotifyIgniting — go straight to burning.
                transition = SetLocked(SessionState.Burning);
            }
        }
        if (transition is { } s) StateChanged?.Invoke(this, s);
    }

    public void NotifyExited()
    {
        SessionState? transition;
        lock (_gate)
        {
            transition = SetLocked(SessionState.Dead);
        }
        if (transition is { } s) StateChanged?.Invoke(this, s);
    }

    public void Tick()
    {
        SessionState? transition = null;
        lock (_gate)
        {
            var now = _clock.UtcNow;
            if (_state == SessionState.Burning && _lastReadAt is { } last)
            {
                if (now - last > _idleThreshold)
                {
                    transition = SetLocked(SessionState.Embers);
                }
            }
            else if (_state == SessionState.Igniting && _ignitedAt is { } ignited)
            {
                if (now - ignited > _ignitingTimeout)
                {
                    transition = SetLocked(SessionState.Embers);
                }
            }
        }
        if (transition is { } s) StateChanged?.Invoke(this, s);
    }

    private SessionState? SetLocked(SessionState next)
    {
        if (_state == SessionState.Dead && next != SessionState.Dead)
        {
            return null;
        }
        if (_state == next)
        {
            return null;
        }
        _state = next;
        return next;
    }
}
