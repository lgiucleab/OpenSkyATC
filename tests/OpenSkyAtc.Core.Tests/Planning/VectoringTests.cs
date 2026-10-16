using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;
using OpenSkyAtc.Core.Random;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Tests.Planning;

public class VectoringTests
{
    /// <summary>Runway 09 at 40N 9E: landing due east, so "left of final" is north.</summary>
    private static readonly RunwayEnd Rwy09 = new("09", new GeoPoint(40, 9), 90);

    private static readonly AirportData Airport = new()
    {
        Icao = "TEST",
        Location = Rwy09.Position,
        Runways = [new Runway(Rwy09, new RunwayEnd("27", GeoMath.Destination(Rwy09.Position, 90, 3000), 270), 45)],
    };

    private static FlightPlanning Planning(int seed = 1) =>
        new(new FlightPlan { Callsign = "T", Origin = "TEST", Destination = "TEST" }, Airport, Airport, new SeededRandom(seed));

    /// <summary>A point x nm out along the extended centreline (west) and y nm right of final (south).</summary>
    private static GeoPoint At(double x, double y) =>
        GeoMath.Destination(GeoMath.Destination(Rwy09.Position, 270, x * GeoMath.MetresPerNm), y >= 0 ? 180 : 0, Math.Abs(y) * GeoMath.MetresPerNm);

    private static SimState State(GeoPoint p, double headingTrue, EngineType engine = EngineType.Jet, double ias = 250) => new()
    {
        Latitude = p.Latitude,
        Longitude = p.Longitude,
        HeadingTrueDeg = headingTrue,
        HeadingMagneticDeg = headingTrue,
        IndicatedAirspeedKt = ias,
        EngineType = engine,
    };

    [Fact]
    public void FrameMatchesTheRunwayGeometry()
    {
        var v = new Vectoring(Rwy09, Planning(), State(At(20, -5), 180));
        var (x, y) = v.Frame(At(20, -5));
        Assert.Equal(20, x, 1);
        Assert.Equal(-5, y, 1);
    }

    [Fact]
    public void FafAndIntercept_AreWithinTheDocRanges_AndBaseLeavesTwoMilesBeforeTheFaf()
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            var p = Planning(seed);
            Assert.InRange(p.FinalApproachFixNm, 8, 12);
            Assert.InRange(p.InterceptAngleDeg, 20, 30);
            var v = new Vectoring(Rwy09, p, State(At(0, -30), 180));
            double interceptAt = v.BaseNm - p.Options.FinalVectorLateralNm / Math.Tan(p.InterceptAngleDeg * Math.PI / 180);
            Assert.Equal(p.FinalApproachFixNm + 2, interceptAt, 6);
        }
    }

    [Fact]
    public void FromTheNorth_DownwindOnTheNorthSide_ThenBase_ThenFinalClosingFromTheLeft()
    {
        var p = Planning();
        var v = new Vectoring(Rwy09, p, State(At(5, -30), 180));
        Assert.Equal(VectorLeg.ToDownwind, v.Leg);

        // Joining: heads for the downwind 5 nm north, abeam.
        var join = v.Update(State(At(5, -30), 180));
        Assert.InRange(join.HeadingMag!.Value, 160, 200);
        Assert.Equal(220, join.SpeedKt);

        var downwind = v.Update(State(At(6, -5), 180));
        Assert.Equal(VectorLeg.Downwind, downwind.Leg);
        Assert.Equal(270, downwind.HeadingMag);

        var baseLeg = v.Update(State(At(v.BaseNm + 0.1, -5), 270));
        Assert.Equal(VectorLeg.Base, baseLeg.Leg);
        Assert.Equal(180, baseLeg.HeadingMag);
        Assert.Equal(180, baseLeg.SpeedKt);

        var final = v.Update(State(At(v.BaseNm + 0.1, -2.4), 180));
        Assert.True(final.IsFinal);
        Assert.False(final.AircraftSideRight); // left of final: "closing localiser from the left"
        Assert.Equal(Vectoring.Magnetic(State(At(0, 0), 0), 90 + p.InterceptAngleDeg), final.HeadingMag);
        Assert.Equal(160, final.SpeedKt);
        Assert.Equal(VectorLeg.Cleared, v.Leg);
    }

    [Fact]
    public void FromTheSouth_DownwindOnTheSouthSide()
    {
        var v = new Vectoring(Rwy09, Planning(), State(At(5, 30), 0));
        var downwind = v.Update(State(At(6, 5), 0));
        Assert.Equal(VectorLeg.Downwind, downwind.Leg);
        var baseLeg = v.Update(State(At(v.BaseNm + 0.1, 5), 270));
        Assert.Equal(360, baseLeg.HeadingMag); // north, towards the centreline
        Assert.True(v.Update(State(At(v.BaseNm + 0.1, 2.4), 0)).AircraftSideRight);
    }

    [Fact]
    public void StraightIn_NoVectors_UntilNearTheFaf_ThenClearanceWithoutAHeading()
    {
        var p = Planning();
        var v = new Vectoring(Rwy09, p, State(At(40, 0.3), 90));
        Assert.Equal(VectorLeg.StraightIn, v.Leg);

        var far = v.Update(State(At(30, 0.3), 90));
        Assert.Null(far.HeadingMag);
        Assert.False(far.IsFinal);

        var near = v.Update(State(At(p.FinalApproachFixNm + 7, 0.3), 90));
        Assert.True(near.IsFinal);
        Assert.Null(near.HeadingMag); // already on the centreline
    }

    [Fact]
    public void AlignedButFarOffTheCentreline_IsNotStraightIn()
    {
        var v = new Vectoring(Rwy09, Planning(), State(At(40, -20), 90));
        Assert.Equal(VectorLeg.ToDownwind, v.Leg);
    }

    [Fact]
    public void NoSpeedControl_ForTurboprops()
    {
        var v = new Vectoring(Rwy09, Planning(), State(At(5, -30), 180, EngineType.Turboprop));
        Assert.Null(v.Update(State(At(5, -30), 180, EngineType.Turboprop)).SpeedKt);
    }

    [Theory]
    [InlineData(92, 0, 90)]
    [InlineData(93, 0, 95)]
    [InlineData(358, 0, 360)]
    [InlineData(100, 4, 95)]   // 4 degrees east variation
    public void Magnetic_RoundsTo5(double trueHeading, double variationEast, int expected)
    {
        var s = new SimState { HeadingTrueDeg = 100, HeadingMagneticDeg = 100 - variationEast };
        Assert.Equal(expected, Vectoring.Magnetic(s, trueHeading));
    }
}
