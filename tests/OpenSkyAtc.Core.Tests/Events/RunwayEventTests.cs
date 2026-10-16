using OpenSkyAtc.Core.Events;
using static OpenSkyAtc.Core.Tests.Events.TestWorld;

namespace OpenSkyAtc.Core.Tests.Events;

public class RunwayEventTests
{
    private static Timeline WithAirport()
    {
        var engine = new EventEngine();
        engine.SetAirports(Airport);
        return new Timeline(engine);
    }

    private static readonly Sim.SimState Taxiing = Parked with { EnginesRunning = [true, true], ParkingBrakeSet = false, GroundSpeedKt = 10 };

    [Fact]
    public void CrossingTheRunway_FiresEnteredThenExited()
    {
        var t = WithAirport()
            .Hold(1, Taxiing.Position(At(1000, 100)))
            .Hold(1, Taxiing.Position(At(1000, 0)))
            .Hold(1, Taxiing.Position(At(1000, -100)));

        Assert.Equal(Runway0927, Assert.Single(t.Of<EnteredRunway>()).Runway);
        Assert.Equal(Runway0927, Assert.Single(t.Of<ExitedRunway>()).Runway);
        Assert.True(t.Events.IndexOf(t.Of<EnteredRunway>()[0]) < t.Events.IndexOf(t.Of<ExitedRunway>()[0]));
    }

    [Fact]
    public void JitterAtTheRunwayEdge_FiresOnce()
    {
        // Half width is 22.5 m; alternate 20 m (inside) and 25 m (inside the 5 m exit margin).
        var t = WithAirport()
            .Hold(1, Taxiing.Position(At(1000, 100)))
            .Run(5, s => Taxiing.Position(At(1000, (int)(s * 4) % 2 == 0 ? 20 : 25)))
            .Hold(1, Taxiing.Position(At(1000, 100)));

        Assert.Single(t.Of<EnteredRunway>());
        Assert.Single(t.Of<ExitedRunway>());
    }

    [Fact]
    public void CurrentRunway_TracksTheRunwayUnderTheAircraft()
    {
        var t = WithAirport().Hold(1, Taxiing.Position(At(1000, 0)));
        Assert.Equal(Runway0927, t.Engine.CurrentRunway);

        t.Hold(1, Taxiing.Position(At(1000, 100)));
        Assert.Null(t.Engine.CurrentRunway);
    }

    [Fact]
    public void Overflight_DoesNotFire()
    {
        var t = WithAirport()
            .Hold(1, Flying.Position(At(1000, 300)))
            .Hold(1, Flying.Position(At(1000, 0)))
            .Hold(1, Flying.Position(At(1000, -300)));

        Assert.Empty(t.Of<EnteredRunway>());
        Assert.Empty(t.Of<ExitedRunway>());
    }

    [Fact]
    public void StartingOnTheRunway_IsBaseline_NotAnEntry()
    {
        var t = WithAirport().Hold(3, Taxiing.Position(At(1000, 0)));
        Assert.Empty(t.Of<EnteredRunway>());
    }
}

public class HoldShortEventTests
{
    private static Timeline WithAirport()
    {
        var engine = new EventEngine();
        engine.SetAirports(Airport);
        return new Timeline(engine);
    }

    private static readonly Sim.SimState Taxiing = Parked with { EnginesRunning = [true, true], ParkingBrakeSet = false, GroundSpeedKt = 10 };

    [Fact]
    public void TaxiingPastTheLineTowardsTheRunway_Fires()
    {
        // The hold-short is 60 m north of the centreline; taxi south from 100 m to 30 m.
        var t = WithAirport().Run(7, s => Taxiing.Position(At(500, 100 - 10 * s)));

        var e = Assert.Single(t.Of<PassedHoldShort>());
        Assert.Equal(Runway0927, e.Runway);
        Assert.Equal(HoldShort, e.HoldShort);
    }

    [Fact]
    public void JitterAtTheLine_FiresOnce()
    {
        var t = WithAirport()
            .Hold(1, Taxiing.Position(At(500, 100)))
            .Run(5, s => Taxiing.Position(At(500, (int)(s * 4) % 2 == 0 ? 57 : 61)));

        Assert.Single(t.Of<PassedHoldShort>());
    }

    [Fact]
    public void PassingFarToTheSide_DoesNotFire()
    {
        var t = WithAirport().Run(7, s => Taxiing.Position(At(600, 100 - 10 * s)));
        Assert.Empty(t.Of<PassedHoldShort>());
    }

    [Fact]
    public void VacatingAwayFromTheRunway_DoesNotFire()
    {
        var t = WithAirport().Run(7, s => Taxiing.Position(At(500, 30 + 10 * s)));
        Assert.Empty(t.Of<PassedHoldShort>());
    }

    [Fact]
    public void InTheAir_DoesNotFire()
    {
        var t = WithAirport().Run(7, s => Flying.Position(At(500, 100 - 10 * s)));
        Assert.Empty(t.Of<PassedHoldShort>());
    }
}

public class TakeoffRollEventTests
{
    private static Timeline WithAirport()
    {
        var engine = new EventEngine();
        engine.SetAirports(Airport);
        return new Timeline(engine);
    }

    private static readonly Sim.SimState LinedUp = Parked.Position(At(100, 0)) with { EnginesRunning = [true, true], ParkingBrakeSet = false };

    [Fact]
    public void AcceleratingThroughFortyKnotsOnARunway_FiresOnce()
    {
        var t = WithAirport()
            .Hold(6, LinedUp)
            .Run(15, s => LinedUp with { GroundSpeedKt = 8 * s });

        var e = Assert.Single(t.Of<TakeoffRoll>());
        Assert.Equal(Runway0927, e.Runway);
    }

    [Fact]
    public void NoisySpeedAroundFortyKnots_FiresOnce()
    {
        var t = WithAirport()
            .Hold(6, LinedUp)
            .Run(10, s => LinedUp with { GroundSpeedKt = 40 + ((int)(s * 4) % 2 == 0 ? 2 : -2) });

        Assert.Single(t.Of<TakeoffRoll>());
    }

    [Fact]
    public void RejectedTakeoff_RearmsOnlyAfterSlowForFiveSeconds()
    {
        var t = WithAirport()
            .Hold(6, LinedUp)
            .Hold(2, LinedUp with { GroundSpeedKt = 60 })
            .Hold(2, LinedUp with { GroundSpeedKt = 20 })  // brief slowdown: not re-armed
            .Hold(2, LinedUp with { GroundSpeedKt = 60 })
            .Hold(6, LinedUp with { GroundSpeedKt = 5 })   // rejected and slowed to taxi speed: re-armed
            .Hold(2, LinedUp with { GroundSpeedKt = 60 });

        Assert.Equal(2, t.Of<TakeoffRoll>().Count);
    }

    [Fact]
    public void FastTaxiOffTheRunway_DoesNotFire()
    {
        var t = WithAirport()
            .Hold(6, LinedUp.Position(At(1000, 100)))
            .Hold(5, LinedUp.Position(At(1000, 100)) with { GroundSpeedKt = 50 });

        Assert.Empty(t.Of<TakeoffRoll>());
    }

    [Fact]
    public void LandingRoll_DoesNotFire()
    {
        // Touch down fast on the runway: not armed, because speed has not been below 30 kt since the takeoff.
        var t = WithAirport()
            .Hold(6, Flying.Position(At(100, 0)) with { GroundSpeedKt = 140 })
            .Hold(10, LinedUp with { GroundSpeedKt = 120 });

        Assert.Empty(t.Of<TakeoffRoll>());
    }
}
