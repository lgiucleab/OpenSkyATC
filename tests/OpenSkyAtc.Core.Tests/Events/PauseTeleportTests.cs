using OpenSkyAtc.Core.Events;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Sim;
using static OpenSkyAtc.Core.Tests.Events.TestWorld;

namespace OpenSkyAtc.Core.Tests.Events;

public class PauseTests
{
    private static readonly SimState Running = Parked with { EnginesRunning = [true, true] };

    [Fact]
    public void Pause_FreezesHoldTimers()
    {
        // Engines on at 1 s, paused from 2 s to 12 s: the 2 s hold completes at 13 s, not 3 s.
        var t = new Timeline()
            .Hold(1, Parked)
            .Hold(1, Running)
            .Hold(10, Running with { Pause = PauseFlags.Full })
            .Hold(3, Running);

        var started = t.Of<EnginesStarted>();
        Assert.Equal(2, started.Count);
        Assert.All(started, e => Assert.Equal(TimeSpan.FromSeconds(13), e.Time));
    }

    [Fact]
    public void Pause_RaisesPausedAndResumedOnce()
    {
        var t = new Timeline()
            .Hold(1, Parked)
            .Hold(5, Parked with { Pause = PauseFlags.Active })
            .Hold(1, Parked);

        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(t.Of<SimPaused>()).Time);
        Assert.Equal(TimeSpan.FromSeconds(6), Assert.Single(t.Of<SimResumed>()).Time);
    }

    [Fact]
    public void ActiveTime_ExcludesPause()
    {
        var t = new Timeline()
            .Hold(4, Parked)
            .Hold(10, Parked with { Pause = PauseFlags.Full })
            .Hold(4, Parked);

        Assert.Equal(TimeSpan.FromSeconds(7.75), t.Engine.ActiveTime);
    }

    [Fact]
    public void ChangesDuringPause_AreNotEvents()
    {
        var t = new Timeline()
            .Hold(2, Flying)
            .Hold(5, Flying with { Pause = PauseFlags.Full, Com1ActiveKhz = 124_700 })
            .Hold(0.25, Flying);

        Assert.Empty(t.Of<FrequencyChanged>());
    }
}

public class TeleportTests
{
    private static SimState Far(SimState s) =>
        s.Position(GeoMath.Destination(new GeoPoint(s.Latitude, s.Longitude), 0, 5 * GeoMath.MetresPerNm));

    [Fact]
    public void JumpOverTwoMiles_RaisesTeleport_AndSuppressesEventsUntilSettled()
    {
        // Jump from the gate to 5 nm away in the air, with new frequency and engines: none of those are events.
        var jumped = Far(Flying) with { Com1ActiveKhz = 124_700 };
        var t = new Timeline()
            .Hold(6, Parked)
            .Hold(12, jumped);

        var tp = Assert.Single(t.Of<Teleported>());
        Assert.Equal(TeleportReason.Jump, tp.Reason);
        Assert.InRange(tp.DistanceNm, 4.9, 5.1);
        Assert.Equal(TimeSpan.FromSeconds(6), tp.Time);
        Assert.Equal(TimeSpan.FromSeconds(16), Assert.Single(t.Of<PositionSettled>()).Time);
        Assert.Equal([typeof(Teleported), typeof(PositionSettled)], t.Events.Select(e => e.GetType()));
    }

    [Fact]
    public void AfterSettling_DetectorsWorkFromAFreshBaseline()
    {
        var jumped = Far(Flying);
        var t = new Timeline()
            .Hold(6, Parked)
            .Hold(12, jumped)
            .Hold(2, jumped with { Com1ActiveKhz = 124_700 });

        Assert.Single(t.Of<FrequencyChanged>());
    }

    [Fact]
    public void SecondJumpWhileSettling_RestartsTheSettleTime()
    {
        var t = new Timeline()
            .Hold(6, Parked)
            .Hold(5, Far(Parked))
            .Hold(12, Far(Far(Parked)));

        Assert.Equal(2, t.Of<Teleported>().Count);
        Assert.Equal(TimeSpan.FromSeconds(21), Assert.Single(t.Of<PositionSettled>()).Time);
    }

    [Fact]
    public void NormalFlight_IsNotATeleport()
    {
        var t = new Timeline().Run(60, s => Flying.Position(GeoMath.Destination(new GeoPoint(41, 2), 90, 250 * 1852 / 3600.0 * s)));
        Assert.Empty(t.Of<Teleported>());
    }

    [Fact]
    public void Slew_SuppressesEvents_AndRaisesTeleportWhenItEnds()
    {
        var t = new Timeline()
            .Hold(2, Parked)
            .Run(4, s => Parked.Position(GeoMath.Destination(new GeoPoint(Parked.Latitude, Parked.Longitude), 90, 250 * s)) with
            {
                SlewActive = true,
                EnginesRunning = [true, true],
            })
            .Hold(12, Parked.Position(GeoMath.Destination(new GeoPoint(Parked.Latitude, Parked.Longitude), 90, 1000)) with { EnginesRunning = [true, true] });

        var tp = Assert.Single(t.Of<Teleported>());
        Assert.Equal(TeleportReason.Slew, tp.Reason);
        Assert.Single(t.Of<PositionSettled>());
        Assert.Empty(t.Of<EnginesStarted>());
    }
}
