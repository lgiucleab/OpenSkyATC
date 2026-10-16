using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;
using OpenSkyAtc.TaxiArrows;

namespace OpenSkyAtc.TaxiArrows.Tests;

/// <summary>Fixture routes in metres east/north of 45N 0E.</summary>
internal static class Routes
{
    public static readonly GeoPoint Origin = new(45, 0);

    public static GeoPoint At(double east, double north) => GeoMath.Destination(GeoMath.Destination(Origin, 90, east), 0, north);

    /// <summary>1,000 m north, then 500 m east; a runway crossing with its hold-short at 150 m.</summary>
    public static TaxiClearance NorthThenEast(bool crossing = true) =>
        new("holding point runway 09", "09", ["A", "B"], crossing ? ["27"] : [], [At(0, 0), At(0, 150), At(0, 1000), At(500, 1000)])
        {
            Crossings = crossing ? [("27", At(0, 150))] : [],
        };

    public static SimState Aircraft(double east, double north) => new() { Latitude = At(east, north).Latitude, Longitude = At(east, north).Longitude, OnGround = true };

    public static FlightSession Session(TaxiClearance? taxi, bool readBack = true, FlightPhase phase = FlightPhase.TaxiOut)
    {
        var s = new FlightSession(1) { Phase = phase };
        s.Clearance.Taxi = taxi;
        if (readBack && taxi is not null) s.Clearance.ConfirmTaxi(taxi);
        return s;
    }
}

public class TaxiPathTests
{
    [Fact]
    public void AtAndAlong_AgreeOnAStraightLeg_AndTurnTheCorner()
    {
        var path = new TaxiPath(Routes.NorthThenEast().Path);
        Assert.Equal(1500, path.LengthM, 0);
        var (p, track) = path.At(400);
        Assert.True(GeoMath.DistanceM(p, Routes.At(0, 400)) < 0.5);
        Assert.Equal(0, track, 1);
        Assert.Equal(90, path.At(1200).TrackDeg, 0);
        Assert.Equal(400, path.Along(Routes.At(3, 400)), 0);
        Assert.Equal(1200, path.Along(Routes.At(200, 1004)), 0);
    }

    [Fact]
    public void Along_NeverLooksBehindTheGivenPoint()
    {
        // Out 300 m north and back south past the start: the point at 100 m north is on both legs.
        var path = new TaxiPath([Routes.At(0, 0), Routes.At(0, 300), Routes.At(10, 300), Routes.At(10, -50)]);
        Assert.Equal(100, path.Along(Routes.At(5, 100)), 0);
        Assert.True(path.Along(Routes.At(5, 100), fromM: 320) > 500);
    }
}

public class TaxiArrowControllerTests
{
    private static readonly TaxiArrowOptions Defaults = new();

    [Fact]
    public void NothingUntilTheRouteIsReadBack()
    {
        var taxi = Routes.NorthThenEast(crossing: false);
        var session = Routes.Session(taxi, readBack: false);
        var c = new TaxiArrowController();
        Assert.Empty(c.Update(session, Routes.Aircraft(0, 0)));
        session.Clearance.ConfirmTaxi(taxi);
        Assert.NotEmpty(c.Update(session, Routes.Aircraft(0, 0)));
    }

    [Fact]
    public void ArrowsEvery10m_UpTo300mAhead_AlongTheRoute()
    {
        var arrows = new TaxiArrowController().Update(Routes.Session(Routes.NorthThenEast(crossing: false)), Routes.Aircraft(0, 0));
        Assert.Equal(30, arrows.Count);                     // 10, 20 ... 300 m
        Assert.True(GeoMath.DistanceM(arrows[0].Position, Routes.At(0, 10)) < 0.5);
        Assert.True(GeoMath.DistanceM(arrows[^1].Position, Routes.At(0, 300)) < 0.5);
        Assert.All(arrows, a => Assert.Equal(0, a.HeadingTrueDeg, 1));
    }

    [Fact]
    public void ArrowsStayPut_AsTheAircraftMoves_AndTheWindowSlides()
    {
        var session = Routes.Session(Routes.NorthThenEast(crossing: false));
        var c = new TaxiArrowController();
        var before = c.Update(session, Routes.Aircraft(0, 100));
        var after = c.Update(session, Routes.Aircraft(1, 125));

        var common = before.Select(a => a.Key).Intersect(after.Select(a => a.Key)).ToList();
        Assert.Equal(27, common.Count);
        Assert.All(common, k => Assert.Equal(before.Single(a => a.Key == k).Position, after.Single(a => a.Key == k).Position));
        Assert.True(GeoMath.DistanceM(after[0].Position, Routes.At(0, 140)) < 0.5, "nothing within 8 m of the aircraft");
        Assert.True(GeoMath.DistanceM(after[^1].Position, Routes.At(0, 420)) < 0.5);
    }

    [Fact]
    public void AtTheCorner_HeadingsFollowTheRoute()
    {
        var arrows = new TaxiArrowController().Update(Routes.Session(Routes.NorthThenEast(crossing: false)), Routes.Aircraft(0, 850));
        Assert.Contains(arrows, a => Math.Abs(a.HeadingTrueDeg) < 1);
        Assert.Contains(arrows, a => Math.Abs(a.HeadingTrueDeg - 90) < 1);
        Assert.True(GeoMath.DistanceM(arrows[^1].Position, Routes.At(150, 1000)) < 0.5);
    }

    [Fact]
    public void AtAHoldShort_ArrowsStop_UntilTheCrossingIsReadBack()
    {
        var taxi = Routes.NorthThenEast();
        var session = Routes.Session(taxi);
        var c = new TaxiArrowController();

        var held = c.Update(session, Routes.Aircraft(0, 0));
        Assert.True(GeoMath.DistanceM(held[^1].Position, Routes.At(0, 150)) < 0.5, "last arrow on the hold-short line");
        Assert.Equal(15, held.Count);

        session.Clearance.ClearToCross("27");
        Assert.Equal(15, c.Update(session, Routes.Aircraft(0, 0)).Count); // cleared but not yet read back
        session.Clearance.ConfirmCrossing("27");
        Assert.Equal(30, c.Update(session, Routes.Aircraft(0, 0)).Count);
    }

    [Fact]
    public void AReRoute_ReplacesTheArrows_AndWaitsForItsReadback()
    {
        var session = Routes.Session(Routes.NorthThenEast(crossing: false));
        var c = new TaxiArrowController();
        var old = c.Update(session, Routes.Aircraft(0, 500));

        var reroute = new TaxiClearance("holding point runway 09", "09", ["C"], [], [Routes.At(0, 500), Routes.At(-400, 500)]);
        session.Clearance.Taxi = reroute;
        Assert.Empty(c.Update(session, Routes.Aircraft(0, 500)));
        session.Clearance.ConfirmTaxi(reroute);
        var fresh = c.Update(session, Routes.Aircraft(0, 500));
        Assert.Empty(fresh.Select(a => a.Key).Intersect(old.Select(a => a.Key)));
        Assert.All(fresh, a => Assert.Equal(270, a.HeadingTrueDeg, 0));

        session.Clearance.ConfirmTaxi(Routes.NorthThenEast()); // a stale readback of another route changes nothing
        Assert.True(session.Clearance.TaxiReadBack);
    }

    [Fact]
    public void AtTheEnd_ArrowsGo_AndDoNotComeBack()
    {
        var session = Routes.Session(Routes.NorthThenEast(crossing: false));
        var c = new TaxiArrowController();
        Assert.NotEmpty(c.Update(session, Routes.Aircraft(400, 1000)));
        Assert.Empty(c.Update(session, Routes.Aircraft(480, 1000)));
        Assert.Empty(c.Update(session, Routes.Aircraft(440, 1000)));
    }

    [Fact]
    public void OutsideTaxiPhases_OrTurnedOff_NoArrows()
    {
        var taxi = Routes.NorthThenEast(crossing: false);
        Assert.Empty(new TaxiArrowController().Update(Routes.Session(taxi, phase: FlightPhase.Holding), Routes.Aircraft(0, 0)));
        Assert.Empty(new TaxiArrowController(Defaults with { Enabled = false }).Update(Routes.Session(taxi), Routes.Aircraft(0, 0)));
        Assert.NotEmpty(new TaxiArrowController().Update(Routes.Session(taxi, phase: FlightPhase.TaxiIn), Routes.Aircraft(0, 0)));
    }

    [Fact]
    public void SettingsApplyImmediately()
    {
        var session = Routes.Session(Routes.NorthThenEast(crossing: false));
        var c = new TaxiArrowController();
        Assert.Equal(30, c.Update(session, Routes.Aircraft(0, 0)).Count);
        c.Options = c.Options with { SpacingM = 5, LookAheadM = 150 };
        Assert.Equal(29, c.Update(session, Routes.Aircraft(0, 0)).Count); // 10, 15 ... 150 m
        c.Options = c.Options with { Enabled = false };
        Assert.Empty(c.Update(session, Routes.Aircraft(0, 0)));
    }
}

public class ArrowPoolTests
{
    private static ArrowPose Pose(long key, double north) => new(key, Routes.At(0, north), 0);

    [Fact]
    public void FirstUpdate_CreatesEach_ThenUnchangedArrowsCostNothing()
    {
        var pool = new ArrowPool();
        var wanted = Enumerable.Range(1, 30).Select(k => Pose(k, k * 10)).ToList();
        Assert.All(pool.Update(wanted), op => Assert.IsType<CreateArrow>(op));
        Assert.Empty(pool.Update(wanted));
    }

    [Fact]
    public void SlidingWindow_MovesTheArrowsLeftBehind_ToTheNewOnesAhead()
    {
        var pool = new ArrowPool();
        pool.Update(Enumerable.Range(1, 30).Select(k => Pose(k, k * 10)).ToList());
        var ops = pool.Update(Enumerable.Range(3, 30).Select(k => Pose(k, k * 10)).ToList());
        Assert.Equal(2, ops.Count);
        Assert.All(ops, op => Assert.IsType<MoveArrow>(op));
        Assert.Equal(30, pool.Count);
    }

    [Fact]
    public void FewerArrows_RemovesTheSurplus_ClearRemovesAll_ResetForgets()
    {
        var pool = new ArrowPool();
        pool.Update(Enumerable.Range(1, 30).Select(k => Pose(k, k * 10)).ToList());
        var ops = pool.Update(Enumerable.Range(1, 15).Select(k => Pose(k, k * 10)).ToList());
        Assert.Equal(15, ops.OfType<RemoveArrow>().Count());
        Assert.Equal(15, pool.Clear().OfType<RemoveArrow>().Count());
        Assert.Equal(0, pool.Count);

        pool.Update([Pose(1, 10)]);
        pool.Reset(); // disconnected: the sim removed them itself
        Assert.IsType<CreateArrow>(Assert.Single(pool.Update([Pose(1, 10)])));
    }
}
