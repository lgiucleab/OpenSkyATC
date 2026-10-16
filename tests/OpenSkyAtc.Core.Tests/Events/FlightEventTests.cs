using OpenSkyAtc.Core.Events;
using OpenSkyAtc.Core.Geo;
using static OpenSkyAtc.Core.Tests.Events.TestWorld;

namespace OpenSkyAtc.Core.Tests.Events;

public class AltitudeCrossedTests
{
    private static readonly object Owner = new();

    private static Sim.SimState At(double feet) => Flying with { IndicatedAltitudeFt = feet, AltitudeMslFt = feet, AltitudeAglFt = feet - 100 };

    [Fact]
    public void ClimbingThroughRegisteredAltitude_FiresOnceWithDirection()
    {
        var t = new Timeline();
        var reg = t.Engine.RegisterAltitude(Owner, 1000, tag: "handoff");
        t.Run(30, s => At(500 + 2000.0 / 60 * s));

        var e = Assert.Single(t.Of<AltitudeCrossed>());
        Assert.Same(reg, e.Registration);
        Assert.True(e.Climbing);
        Assert.Equal("handoff", e.Registration.Tag);
    }

    [Fact]
    public void Descending_ReportsNotClimbing()
    {
        var t = new Timeline();
        t.Engine.RegisterAltitude(Owner, 1000);
        t.Run(30, s => At(1500 - 2000.0 / 60 * s));

        Assert.False(Assert.Single(t.Of<AltitudeCrossed>()).Climbing);
    }

    [Fact]
    public void NoiseAroundTheValue_FiresOnce()
    {
        var t = new Timeline();
        t.Engine.RegisterAltitude(Owner, 1000);
        t.Hold(1, At(900))
         .Run(20, s => At(1000 + ((int)(s * 4) % 2 == 0 ? 20 : -20)));

        Assert.Single(t.Of<AltitudeCrossed>());
    }

    [Fact]
    public void AboveGroundReference_UsesAgl()
    {
        var t = new Timeline();
        t.Engine.RegisterAltitude(Owner, 1000, AltitudeReference.AboveGround);
        t.Hold(1, At(1000)) // AGL 900
         .Hold(1, At(1200)); // AGL 1100

        Assert.Single(t.Of<AltitudeCrossed>());
    }

    [Fact]
    public void AlreadyPastWhenRegistered_DoesNotFire()
    {
        var t = new Timeline();
        t.Engine.RegisterAltitude(Owner, 1000);
        t.Run(10, s => At(1500 + 30 * s));

        Assert.Empty(t.Of<AltitudeCrossed>());
    }

    [Fact]
    public void UnregisterAll_RemovesOnlyThatOwner()
    {
        var other = new object();
        var t = new Timeline();
        t.Engine.RegisterAltitude(Owner, 1000);
        var kept = t.Engine.RegisterAltitude(other, 1000);
        t.Engine.UnregisterAll(Owner);
        t.Hold(1, At(900)).Hold(1, At(1100));

        Assert.Same(kept, Assert.Single(t.Of<AltitudeCrossed>()).Registration);
    }
}

public class LevelOffTests
{
    private static Sim.SimState Climbing => Flying with { VerticalSpeedFpm = 2000 };

    [Fact]
    public void LevelForTenSeconds_FiresOnce_WithAltitude()
    {
        var t = new Timeline()
            .Hold(6, Climbing)
            .Hold(12, Flying with { VerticalSpeedFpm = 0, IndicatedAltitudeFt = 7000 });

        var e = Assert.Single(t.Of<LevelOff>());
        Assert.Equal(7000, e.AltitudeFt);
        Assert.Equal(TimeSpan.FromSeconds(16), e.Time);
    }

    [Fact]
    public void LevelLessThanTenSeconds_DoesNotFire()
    {
        var t = new Timeline()
            .Hold(6, Climbing)
            .Hold(9, Flying with { VerticalSpeedFpm = 0 })
            .Hold(6, Climbing);

        Assert.Empty(t.Of<LevelOff>());
    }

    [Fact]
    public void NoisyVerticalSpeedWithinBand_FiresOnce()
    {
        var t = new Timeline()
            .Hold(6, Climbing)
            .Run(40, s => Flying with { VerticalSpeedFpm = (int)(s * 4) % 2 == 0 ? 180 : -180 });

        Assert.Single(t.Of<LevelOff>());
    }

    [Fact]
    public void StepClimb_FiresAgainAfterRearm()
    {
        var t = new Timeline()
            .Hold(6, Climbing)
            .Hold(12, Flying with { VerticalSpeedFpm = 0 })
            .Hold(6, Climbing)
            .Hold(12, Flying with { VerticalSpeedFpm = 0 });

        Assert.Equal(2, t.Of<LevelOff>().Count);
    }

    [Fact]
    public void OnGround_DoesNotFire()
    {
        var t = new Timeline().Hold(20, Parked);
        Assert.Empty(t.Of<LevelOff>());
    }
}

public class DistanceCrossedTests
{
    private static readonly object Owner = new();
    private static readonly GeoPoint Destination = new(41.0, 2.0);

    private static Sim.SimState FromDestination(double nm) =>
        Flying.Position(GeoMath.Destination(Destination, 270, nm * GeoMath.MetresPerNm));

    [Fact]
    public void ApproachingInsideRegisteredDistance_FiresInbound()
    {
        var t = new Timeline();
        t.Engine.RegisterDistance(Owner, Destination, 10, "tod");
        t.Run(80, s => FromDestination(11 - 250.0 / 3600 * s)); // 250 kt inbound, 11 nm to about 5.4 nm

        var e = Assert.Single(t.Of<DistanceCrossed>());
        Assert.True(e.Inbound);
        Assert.Equal("tod", e.Registration.Tag);
    }

    [Fact]
    public void MovingAway_FiresOutbound()
    {
        var t = new Timeline();
        t.Engine.RegisterDistance(Owner, Destination, 10);
        t.Run(80, s => FromDestination(9 + 250.0 / 3600 * s));

        Assert.False(Assert.Single(t.Of<DistanceCrossed>()).Inbound);
    }

    [Fact]
    public void JitterAroundTheDistance_FiresOnce()
    {
        var t = new Timeline();
        t.Engine.RegisterDistance(Owner, Destination, 10);
        t.Hold(1, FromDestination(10.5))
         .Run(20, s => FromDestination(10 + ((int)(s * 4) % 2 == 0 ? 0.05 : -0.05)));

        Assert.Single(t.Of<DistanceCrossed>());
    }
}

public class EstablishedTests
{
    private static readonly object Owner = new();
    private static readonly Model.RunwayEnd Rwy09 = Runway0927.First;

    /// <summary>Airborne west of runway 09, <paramref name="nm"/> out, <paramref name="offsetDeg"/> off the extended centreline.</summary>
    private static Sim.SimState OnFinal(double nm, double offsetDeg = 0) =>
        Flying.Position(GeoMath.Destination(Rwy09.Position, 270 + offsetDeg, nm * GeoMath.MetresPerNm));

    private static double Inbound(double s) => 10 - 150.0 / 3600 * s; // 150 kt towards the runway

    [Fact]
    public void OnCourseAndClosing_FiresOnce()
    {
        var t = new Timeline();
        var reg = t.Engine.RegisterEstablished(Owner, Rwy09);
        t.Run(20, s => OnFinal(Inbound(s), 0.5));

        var e = Assert.Single(t.Of<Established>());
        Assert.Same(reg, e.Registration);
        Assert.Equal(90, e.Registration.CourseTrueDeg);
    }

    [Fact]
    public void OutsideOneDegree_DoesNotFire()
    {
        var t = new Timeline();
        t.Engine.RegisterEstablished(Owner, Rwy09);
        t.Run(20, s => OnFinal(Inbound(s), 1.5));

        Assert.Empty(t.Of<Established>());
    }

    [Fact]
    public void MovingAwayFromTheRunway_DoesNotFire()
    {
        var t = new Timeline();
        t.Engine.RegisterEstablished(Owner, Rwy09);
        t.Run(20, s => OnFinal(5 + 150.0 / 3600 * s));

        Assert.Empty(t.Of<Established>());
    }

    [Fact]
    public void WeavingAroundTheEdge_FiresOnce()
    {
        var t = new Timeline();
        t.Engine.RegisterEstablished(Owner, Rwy09);
        t.Run(4, s => OnFinal(Inbound(s), 0.8))
         .Run(20, s => OnFinal(Inbound(4 + s), (int)(s * 4) % 2 == 0 ? 0.8 : 1.5));

        Assert.Single(t.Of<Established>());
    }

    [Fact]
    public void BeyondMaxDistance_DoesNotFire()
    {
        var t = new Timeline();
        t.Engine.RegisterEstablished(Owner, Rwy09, maxDistanceNm: 25);
        t.Run(20, s => OnFinal(30 - 150.0 / 3600 * s));

        Assert.Empty(t.Of<Established>());
    }

    [Fact]
    public void AlreadyEstablishedWhenRegistered_Fires()
    {
        var t = new Timeline().Run(5, s => OnFinal(Inbound(s)));
        t.Engine.RegisterEstablished(Owner, Rwy09);
        t.Run(5, s => OnFinal(Inbound(5 + s)));

        Assert.Single(t.Of<Established>());
    }
}
