using Firepit.Core.Sessions;
using Firepit.Core.Time;

namespace Firepit.Core.Tests;

public class ActivityDetectorTests
{
    private static readonly ActivitySettings TestSettings = new(IdleThresholdMs: 1500, IgnitingTimeoutMs: 10_000);

    [Fact]
    public void Initial_StateIsCold()
    {
        var detector = new ActivityDetector(new FakeClock());
        Assert.Equal(SessionState.Cold, detector.State);
    }

    [Fact]
    public void Igniting_TransitionsFromColdOnNotifyIgniting()
    {
        var detector = new ActivityDetector(new FakeClock(), TestSettings);
        detector.NotifyIgniting();
        Assert.Equal(SessionState.Igniting, detector.State);
    }

    [Fact]
    public void Burning_TransitionsFromIgnitingOnFirstRead()
    {
        var detector = new ActivityDetector(new FakeClock(), TestSettings);
        detector.NotifyIgniting();
        detector.NotifyRead();
        Assert.Equal(SessionState.Burning, detector.State);
    }

    [Fact]
    public void Embers_TransitionsFromBurningAfterIdleThreshold()
    {
        var clock = new FakeClock();
        var detector = new ActivityDetector(clock, TestSettings);
        detector.NotifyIgniting();
        detector.NotifyRead();

        // Idle just under the threshold — still burning.
        clock.Advance(TimeSpan.FromMilliseconds(1400));
        detector.Tick();
        Assert.Equal(SessionState.Burning, detector.State);

        // Past the threshold — embers.
        clock.Advance(TimeSpan.FromMilliseconds(200));
        detector.Tick();
        Assert.Equal(SessionState.Embers, detector.State);
    }

    [Fact]
    public void Burning_RestoredFromEmbersOnReadWithoutFlicker()
    {
        var clock = new FakeClock();
        var detector = new ActivityDetector(clock, TestSettings);
        detector.NotifyIgniting();
        detector.NotifyRead();

        // Sit on burning for sub-threshold.
        clock.Advance(TimeSpan.FromMilliseconds(500));
        detector.Tick();
        Assert.Equal(SessionState.Burning, detector.State);

        // 600 ms more reads (each within threshold) — must NOT flicker to embers.
        var states = new List<SessionState>();
        detector.StateChanged += (_, s) => states.Add(s);
        for (var i = 0; i < 6; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
            detector.Tick();
            detector.NotifyRead();
        }
        Assert.Empty(states); // no transitions emitted during steady-state burning
        Assert.Equal(SessionState.Burning, detector.State);
    }

    [Fact]
    public void Igniting_TimesOutToEmbersIfNoReadByTimeout()
    {
        var clock = new FakeClock();
        var detector = new ActivityDetector(clock, new ActivitySettings(IdleThresholdMs: 1500, IgnitingTimeoutMs: 1000));
        detector.NotifyIgniting();

        clock.Advance(TimeSpan.FromMilliseconds(1100));
        detector.Tick();

        Assert.Equal(SessionState.Embers, detector.State);
    }

    [Fact]
    public void Dead_FromAnyState()
    {
        var detector = new ActivityDetector(new FakeClock());
        detector.NotifyIgniting();
        detector.NotifyRead();
        detector.NotifyExited();
        Assert.Equal(SessionState.Dead, detector.State);
    }

    [Fact]
    public void Dead_StaysDeadIgnoringFurtherEvents()
    {
        var detector = new ActivityDetector(new FakeClock());
        detector.NotifyIgniting();
        detector.NotifyExited();
        detector.NotifyRead();
        Assert.Equal(SessionState.Dead, detector.State);
    }

    [Fact]
    public void StateChanged_DoesNotFireForRedundantTransitions()
    {
        var detector = new ActivityDetector(new FakeClock(), TestSettings);
        var transitions = new List<SessionState>();
        detector.StateChanged += (_, s) => transitions.Add(s);

        detector.NotifyIgniting();
        detector.NotifyIgniting();
        detector.NotifyRead();
        detector.NotifyRead();

        Assert.Equal([SessionState.Igniting, SessionState.Burning], transitions);
    }

    [Fact]
    public void Progress_PinsBurningPastIdleThreshold()
    {
        var clock = new FakeClock();
        var detector = new ActivityDetector(clock, TestSettings);
        detector.NotifyIgniting();
        detector.NotifyRead();
        Assert.Equal(SessionState.Burning, detector.State);

        // Agent reports thinking — no bytes will flow for a while.
        detector.NotifyProgress(active: true);

        // Sit far past the idle threshold without any reads.
        clock.Advance(TimeSpan.FromMilliseconds(5000));
        detector.Tick();

        // Without progress pinning, this would have demoted to Embers at 1500ms.
        Assert.Equal(SessionState.Burning, detector.State);
    }

    [Fact]
    public void Progress_ResumesNormalDecayWhenCleared()
    {
        var clock = new FakeClock();
        var detector = new ActivityDetector(clock, TestSettings);
        detector.NotifyIgniting();
        detector.NotifyRead();
        detector.NotifyProgress(active: true);

        // Long thinking pause.
        clock.Advance(TimeSpan.FromMilliseconds(5000));
        detector.Tick();
        Assert.Equal(SessionState.Burning, detector.State);

        // Agent clears progress — threshold timer restarts from now.
        detector.NotifyProgress(active: false);

        // Just under threshold from the clear: still burning.
        clock.Advance(TimeSpan.FromMilliseconds(1400));
        detector.Tick();
        Assert.Equal(SessionState.Burning, detector.State);

        // Past threshold: decays to embers.
        clock.Advance(TimeSpan.FromMilliseconds(200));
        detector.Tick();
        Assert.Equal(SessionState.Embers, detector.State);
    }

    [Fact]
    public void Progress_LiftsEmbersBackToBurning()
    {
        var clock = new FakeClock();
        var detector = new ActivityDetector(clock, TestSettings);
        detector.NotifyIgniting();
        detector.NotifyRead();

        // Decay to embers via idle.
        clock.Advance(TimeSpan.FromMilliseconds(2000));
        detector.Tick();
        Assert.Equal(SessionState.Embers, detector.State);

        // Late progress signal — the agent woke up. Lift back to burning.
        detector.NotifyProgress(active: true);
        Assert.Equal(SessionState.Burning, detector.State);
    }

    [Fact]
    public void Progress_RedundantSignalDoesNotFireStateChanged()
    {
        var detector = new ActivityDetector(new FakeClock(), TestSettings);
        detector.NotifyIgniting();
        detector.NotifyRead();
        var transitions = new List<SessionState>();
        detector.StateChanged += (_, s) => transitions.Add(s);

        detector.NotifyProgress(active: true);
        detector.NotifyProgress(active: true);
        detector.NotifyProgress(active: true);

        // Already burning — true→true→true should produce no transitions.
        Assert.Empty(transitions);
    }

    [Fact]
    public void Progress_IgnoredWhenDead()
    {
        var detector = new ActivityDetector(new FakeClock());
        detector.NotifyIgniting();
        detector.NotifyExited();
        detector.NotifyProgress(active: true);
        Assert.Equal(SessionState.Dead, detector.State);
    }

    private sealed class FakeClock : IActivityClock
    {
        public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 5, 8, 10, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan delta) => UtcNow = UtcNow + delta;
    }
}
