using OpenSkyAtc.Core.Events;
using static OpenSkyAtc.Core.Tests.Events.TestWorld;

namespace OpenSkyAtc.Core.Tests.Events;

public class EngineEventTests
{
    [Fact]
    public void EngineStart_FiresAfterHold_WithRunningCount()
    {
        var t = new Timeline()
            .Hold(1, Parked)
            .Hold(3, Parked with { EnginesRunning = [true, false] });

        var e = Assert.Single(t.Of<EnginesStarted>());
        Assert.Equal(1, e.EngineIndex);
        Assert.Equal(1, e.RunningCount);
        Assert.Equal(TimeSpan.FromSeconds(3), e.Time); // on at 1.0 s, held 2 s
    }

    [Fact]
    public void EngineStop_Fires()
    {
        var t = new Timeline()
            .Hold(1, Parked with { EnginesRunning = [true, true] })
            .Hold(3, Parked with { EnginesRunning = [true, false] });

        var e = Assert.Single(t.Of<EnginesStopped>());
        Assert.Equal(2, e.EngineIndex);
        Assert.Equal(1, e.RunningCount);
    }

    [Fact]
    public void EngineFlickerShorterThanHold_DoesNotFire()
    {
        var t = new Timeline()
            .Hold(1, Parked)
            .Hold(1.5, Parked with { EnginesRunning = [true, false] })
            .Hold(3, Parked);

        Assert.Empty(t.Of<EnginesStarted>());
        Assert.Empty(t.Of<EnginesStopped>());
    }

    [Fact]
    public void EnginesAlreadyRunningAtStart_DoNotFire()
    {
        var t = new Timeline().Hold(5, Parked with { EnginesRunning = [true, true] });
        Assert.Empty(t.Events);
    }
}

public class TaxiEventTests
{
    [Fact]
    public void TaxiStarted_FiresAfterThreeSecondsAboveThreshold()
    {
        var t = new Timeline()
            .Hold(6, Parked)
            .Hold(4, Parked with { GroundSpeedKt = 5 });

        var e = Assert.Single(t.Of<TaxiStarted>());
        Assert.Equal(TimeSpan.FromSeconds(9), e.Time);
    }

    [Fact]
    public void NoisySpeedAroundThreshold_FiresOnce()
    {
        var t = new Timeline()
            .Hold(6, Parked)
            .Run(10, s => Parked with { GroundSpeedKt = 0.6 * s + Noise(s) }) // ramp through 3 kt with ±0.5 kt noise
            .Run(10, s => Parked with { GroundSpeedKt = 3.2 + Noise(s) });   // slow taxi, dipping below 3 kt

        Assert.Single(t.Of<TaxiStarted>());
    }

    [Fact]
    public void BriefMovement_DoesNotFire()
    {
        var t = new Timeline()
            .Hold(6, Parked)
            .Hold(2, Parked with { GroundSpeedKt = 5 })
            .Hold(2, Parked);

        Assert.Empty(t.Of<TaxiStarted>());
    }

    [Fact]
    public void RearmsOnlyAfterStoppedForFiveSeconds()
    {
        var t = new Timeline()
            .Hold(6, Parked)
            .Hold(4, Parked with { GroundSpeedKt = 10 })
            .Hold(2, Parked)                               // short stop: not re-armed
            .Hold(4, Parked with { GroundSpeedKt = 10 })
            .Hold(6, Parked)                               // long stop: re-armed
            .Hold(4, Parked with { GroundSpeedKt = 10 });

        Assert.Equal(2, t.Of<TaxiStarted>().Count);
    }

    [Fact]
    public void SpeedInTheAir_DoesNotCount()
    {
        var t = new Timeline().Hold(10, Flying);
        Assert.Empty(t.Of<TaxiStarted>());
    }

    internal static double Noise(double s) => (int)Math.Round(s * 4) % 2 == 0 ? 0.5 : -0.5;
}

public class AirborneTouchdownTests
{
    [Fact]
    public void Airborne_FiresAfterThreeSeconds_WithLiftOffTime()
    {
        var t = new Timeline()
            .Hold(6, Parked)
            .Hold(4, Parked with { OnGround = false });

        var e = Assert.Single(t.Of<Airborne>());
        Assert.Equal(TimeSpan.FromSeconds(6), e.LiftOff);
        Assert.Equal(TimeSpan.FromSeconds(9), e.Time);
    }

    [Fact]
    public void BounceShorterThanHold_DoesNotFire_ThenRealLiftOffFiresOnce()
    {
        var t = new Timeline()
            .Hold(6, Parked)
            .Hold(2, Parked with { OnGround = false })
            .Hold(1, Parked)
            .Hold(4, Parked with { OnGround = false });

        var e = Assert.Single(t.Of<Airborne>());
        Assert.Equal(TimeSpan.FromSeconds(9), e.LiftOff);
    }

    [Fact]
    public void DoesNotRearm_UntilOnGroundForFiveSeconds()
    {
        var t = new Timeline()
            .Hold(6, Parked)
            .Hold(4, Parked with { OnGround = false })
            .Hold(2, Parked)
            .Hold(4, Parked with { OnGround = false });

        Assert.Single(t.Of<Airborne>());
    }

    [Fact]
    public void SessionStartingInTheAir_DoesNotFireAirborne()
    {
        var t = new Timeline().Hold(10, Flying);
        Assert.Empty(t.Of<Airborne>());
    }

    [Fact]
    public void Touchdown_FiresAfterThreeSeconds_WithFirstContact()
    {
        var t = new Timeline()
            .Hold(6, Flying)
            .Hold(4, Flying with { OnGround = true });

        var e = Assert.Single(t.Of<Touchdown>());
        Assert.Equal(TimeSpan.FromSeconds(6), e.FirstContact);
        Assert.Equal(TimeSpan.FromSeconds(9), e.Time);
    }

    [Fact]
    public void BouncedLanding_FiresOnce_AtFinalContact()
    {
        var t = new Timeline()
            .Hold(6, Flying)
            .Hold(1, Flying with { OnGround = true })
            .Hold(1, Flying)
            .Hold(4, Flying with { OnGround = true });

        var e = Assert.Single(t.Of<Touchdown>());
        Assert.Equal(TimeSpan.FromSeconds(8), e.FirstContact);
    }

    [Fact]
    public void SessionStartingOnGround_DoesNotFireTouchdown()
    {
        var t = new Timeline().Hold(10, Parked);
        Assert.Empty(t.Of<Touchdown>());
    }
}
