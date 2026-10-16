using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;
using OpenSkyAtc.Replay.Tests.Harness;
using static OpenSkyAtc.Replay.Tests.Harness.SyntheticRoute;

namespace OpenSkyAtc.Replay.Tests;

/// <summary>Sessions that start away from the gate, and teleports (design doc edge cases).</summary>
public class PhaseInferenceTests
{
    private static AtcBrain Start()
    {
        var brain = new AtcBrain(new AtcBrainOptions { CenterFrequencies = Centers });
        brain.Start(Plan, Origin, Destination, 5);
        return brain;
    }

    private static SimState At(GeoPoint p, double altFt, bool onGround = false, double heading = 90) => new()
    {
        Latitude = p.Latitude,
        Longitude = p.Longitude,
        AltitudeMslFt = altFt,
        AltitudeAglFt = altFt,
        IndicatedAltitudeFt = altFt,
        IndicatedAirspeedKt = onGround ? 0 : 280,
        GroundSpeedKt = onGround ? 0 : 400,
        HeadingTrueDeg = heading,
        HeadingMagneticDeg = heading,
        OnGround = onGround,
        EnginesRunning = [true, true],
        Com1ActiveKhz = 132_350,
        TransponderMode = TransponderMode.Alt,
    };

    private static void Feed(AtcBrain brain, SimState s, double seconds, ref TimeSpan t)
    {
        for (int i = 0; i < seconds * 4; i++)
        {
            brain.Tick(s with { Time = t }, t);
            t += TimeSpan.FromMilliseconds(250);
        }
    }

    [Fact]
    public void StartingEnRoute_InfersEnRoute_WithAFreshClearanceToTheCurrentAltitude()
    {
        var brain = Start();
        var t = TimeSpan.Zero;
        Feed(brain, At(GeoMath.Destination(OriginThreshold, 90, 150 * GeoMath.MetresPerNm), 23_800), 2, ref t);

        Assert.Equal(FlightPhase.EnRoute, brain.Session.Phase);
        Assert.Equal(AltitudeTarget.FlightLevel(240), brain.Session.Clearance.ClearedAltitude);
        Assert.NotNull(brain.Session.Clearance.Squawk);
        Assert.Single(brain.Session.Log.Entries.OfType<PhaseInferred>());
    }

    [Theory]
    [InlineData(20, 8000, FlightPhase.Departure)]
    [InlineData(395, 5000, FlightPhase.Approach)]
    public void StartingNearAnAirport_InTheAir(double nmFromOrigin, double altFt, FlightPhase expected)
    {
        var brain = Start();
        var t = TimeSpan.Zero;
        Feed(brain, At(GeoMath.Destination(OriginThreshold, 90, nmFromOrigin * GeoMath.MetresPerNm), altFt), 2, ref t);
        Assert.Equal(expected, brain.Session.Phase);
    }

    [Fact]
    public void StartingOnTheRunway_IsHolding_WithThatRunway()
    {
        var brain = Start();
        var t = TimeSpan.Zero;
        Feed(brain, At(GeoMath.Destination(OriginThreshold, 90, 100), 0, onGround: true), 2, ref t);
        Assert.Equal(FlightPhase.Holding, brain.Session.Phase);
        Assert.Equal("09", brain.Session.Clearance.Runway);
    }

    [Fact]
    public void StartingAtTheDestinationGate_IsTaxiIn()
    {
        var brain = Start();
        var t = TimeSpan.Zero;
        Feed(brain, At(DestinationStand, 0, onGround: true), 2, ref t);
        Assert.Equal(FlightPhase.TaxiIn, brain.Session.Phase);
    }

    [Fact]
    public void Teleport_ReinfersThePhase_AfterSettling()
    {
        var brain = Start();
        var t = TimeSpan.Zero;
        Feed(brain, At(OriginStand, 0, onGround: true, heading: 180), 5, ref t);
        Assert.Equal(FlightPhase.Preflight, brain.Session.Phase);

        Feed(brain, At(GeoMath.Destination(OriginThreshold, 90, 200 * GeoMath.MetresPerNm), 24_000), 12, ref t);

        Assert.Equal(FlightPhase.EnRoute, brain.Session.Phase);
        Assert.Contains(brain.Session.Log.Entries.OfType<PhaseInferred>(), p => p.Reason.StartsWith("repositioned"));
    }
}
