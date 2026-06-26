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
    private bool _progressActive;

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

    /// <summary>
    /// The agent is reporting in-progress work via OSC 9;4 (Claude Code emits
    /// state=3 indeterminate during thinking and tool calls, state=0 to clear).
    /// While active, Burning is pinned regardless of byte-stream idle timeout —
    /// thinking can produce no output for many seconds, but the session is not idle.
    /// </summary>
    public void NotifyProgress(bool active)
    {
        SessionState? transition = null;
        lock (_gate)
        {
            if (_progressActive == active)
            {
                return;
            }
            _progressActive = active;

            if (_state == SessionState.Dead)
            {
                return;
            }

            // Refresh the read timestamp on every progress flip so the threshold
            // timer doesn't snap state immediately after the transition.
            _lastReadAt = _clock.UtcNow;

            if (active && _state != SessionState.Burning)
            {
                transition = SetLocked(SessionState.Burning);
            }
        }
        if (transition is { } s) StateChanged?.Invoke(this, s);
    }

    public void Tick()
    {
        SessionState? transition = null;
        lock (_gate)
        {
            var now = _clock.UtcNow;
            if (_state == SessionState.Burning && _lastReadAt is { } last && !_progressActive)
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
        // Dead is terminal for PASSIVE transitions — a late NotifyRead from a
        // torn-down pump, or a Tick — so a dying session can't flicker back to
        // life on stray events. But NotifyIgniting is the explicit "a NEW agent
        // process is booting" signal, fired before every (re)spawn. It MUST be
        // able to revive a Dead detector: the detector lives for the whole tab,
        // not per-process, so without this escape it stays Dead across every
        // restart. A stuck-Dead detector then makes each later tab activation
        // tear down and respawn a perfectly live agent — the "switching to this
        // tab kills the session and redraws everything" bug.
        if (_state == SessionState.Dead && next != SessionState.Dead && next != SessionState.Igniting)
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
