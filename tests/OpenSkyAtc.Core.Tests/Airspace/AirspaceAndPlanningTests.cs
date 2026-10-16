using OpenSkyAtc.Core.Airspace;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;
using OpenSkyAtc.Core.Random;

namespace OpenSkyAtc.Core.Tests.Airspace;

/// <summary>A straight route due east along 40N; distances in nm are easy to reason about.</summary>
internal static class Route
{
    public static GeoPoint East(double nm) => GeoMath.Destination(new GeoPoint(40, 0), 90, nm * GeoMath.MetresPerNm);

    public static AirportData Airport(string icao, double nmEast) => new() { Icao = icao, Name = icao, Location = East(nmEast), ElevationFt = 400 };

    public static FlightPlan Plan(double lengthNm) => new()
    {
        Callsign = "VLG1234",
        Origin = "AAAA",
        Destination = "BBBB",
        CruiseLevel = AltitudeTarget.FlightLevel(350),
        Route = [new RouteWaypoint("MID", East(lengthNm / 2))],
    };

    public static AirspaceModel Build(double lengthNm, params CenterFrequency[] centers) =>
        AirspaceModel.Build(Airport("AAAA", 0), Airport("BBBB", lengthNm), Plan(lengthNm), centers, new SeededRandom(1));

    public static CenterFrequency Center(int khz, double nmEast, string? channel = null, double nmNorth = 20) =>
        new(khz, GeoMath.Destination(East(nmEast), 0, nmNorth * GeoMath.MetresPerNm), "XXXX", channel, "Somewhere");
}

public class RouteLineTests
{
    [Fact]
    public void AlongAndRemaining_FollowTheRoute()
    {
        var model = Route.Build(400);
        Assert.Equal(400, model.Route.LengthNm, 0);
        Assert.Equal(150, model.Route.AlongNm(Route.East(150)), 0);
        Assert.Equal(150, model.Route.AlongNm(GeoMath.Destination(Route.East(150), 0, 10 * GeoMath.MetresPerNm)), 0); // 10 nm abeam
        Assert.Equal(250, model.Route.RemainingNm(Route.East(150)), 0);
        Assert.Equal(200, GeoMath.DistanceNm(model.Route.PointAt(200), Route.East(0)), 0);
    }
}

public class CtrSectorTests
{
    [Fact]
    public void SectorsCoverTheRouteBetweenTheTerminalAreas_WithLengths150To250()
    {
        var model = Route.Build(800, Route.Center(132_350, 200, "ALPHA CENTER"), Route.Center(128_500, 450, "BRAVO CTR"), Route.Center(125_000, 650));
        var s = model.Sectors;

        Assert.NotEmpty(s);
        Assert.Equal(40, s[0].StartNm, 1);
        Assert.Equal(760, s[^1].EndNm, 1);
        for (int i = 1; i < s.Count; i++) Assert.Equal(s[i - 1].EndNm, s[i].StartNm, 6);
        Assert.All(s, x => Assert.Equal(ControllerRole.Center, x.Unit.Role));
    }

    [Fact]
    public void EachSector_UsesTheNearestCenterFrequency_AndNeighboursOnTheSameFrequencyMerge()
    {
        var model = Route.Build(400, Route.Center(132_350, 150, "ALPHA CENTER"));
        var only = Assert.Single(model.Sectors); // every sector picked the only frequency, so they merged
        Assert.Equal(132_350, only.Unit.FrequencyKhz);
        Assert.Equal("Alpha Control", only.Unit.Callsign);
    }

    [Fact]
    public void FrequenciesFurtherThan300nm_AreNotUsed_AndNoDataMeansNoSectors()
    {
        Assert.Empty(Route.Build(400, Route.Center(132_350, 200, nmNorth: 400)).Sectors);
        Assert.Empty(Route.Build(400).Sectors);
    }

    [Fact]
    public void SectorWithoutAFrequency_TakesItsNeighbours()
    {
        // Only the start of a long route has data: the later sectors fall back to it (merged into one).
        var model = Route.Build(1500, Route.Center(132_350, 100));
        Assert.Equal(132_350, Assert.Single(model.Sectors).Unit.FrequencyKhz);
        Assert.Equal(1460, model.Sectors[0].EndNm, 1);
    }

    [Fact]
    public void ShortFlights_HaveNoCtr()
    {
        Assert.Empty(Route.Build(75, Route.Center(132_350, 40)).Sectors);
    }

    [Theory]
    [InlineData("MADRID CENTER", "Madrid Control")]
    [InlineData("BREST ACC", "Brest Control")]
    [InlineData("LONDON", "London Control")]
    [InlineData(null, "Somewhere Control")]
    public void SectorNames_UseTheIcaoTerm(string? channel, string expected) =>
        Assert.Equal(expected, AirspaceModel.SectorName(new CenterFrequency(1, new GeoPoint(0, 0), "X", channel, "Somewhere")));

    [Fact]
    public void ArrivalAndDepartureVolumes()
    {
        var model = Route.Build(400);
        Assert.True(model.InDepartureVolume(Route.East(30), 9000));
        Assert.False(model.InDepartureVolume(Route.East(30), 16000));
        Assert.False(model.InDepartureVolume(Route.East(50), 9000));
        Assert.True(model.InArrivalVolume(Route.East(370), 20000));   // within 40 nm
        Assert.True(model.InArrivalVolume(Route.East(300), 12000));   // below FL150 and inbound
        Assert.False(model.InArrivalVolume(Route.East(100), 12000));  // below FL150 but outbound half
    }
}

public class FlightPlanningTests
{
    private static FlightPlanning Planning(int seed = 1, AltitudeTarget? cruise = null, AirportData? destination = null)
    {
        var plan = Route.Plan(400) with { CruiseLevel = cruise ?? AltitudeTarget.FlightLevel(350) };
        return new FlightPlanning(plan, Route.Airport("AAAA", 0), destination ?? Route.Airport("BBBB", 400), new SeededRandom(seed));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(9)]
    public void ClimbSteps_AscendInWholeThousands_AndEndAtCruise(int seed)
    {
        var steps = Planning(seed).ClimbSteps;
        Assert.InRange(steps.Count, 1, 6);
        Assert.Equal(AltitudeTarget.FlightLevel(350), steps[^1]);
        Assert.All(steps, s => Assert.Equal(0, s.NominalFeet % 1000));
        Assert.Equal(steps.OrderBy(s => s.NominalFeet), steps);
        Assert.True(steps[0].NominalFeet > 5000);
    }

    [Fact]
    public void NextClimbStep_IsTheFirstAboveTheClearedLevel()
    {
        var p = Planning();
        Assert.Equal(p.ClimbSteps[0], p.NextClimbStep(AltitudeTarget.Feet(5000)));
        Assert.Null(p.NextClimbStep(AltitudeTarget.FlightLevel(350)));
    }

    [Theory]
    [InlineData(35_000, 3_000, 106)]
    [InlineData(10_000, 3_000, 31)]
    [InlineData(3_000, 3_000, 10)]
    public void TopOfDescent_FollowsTheDocFormula(int cruise, int target, double expected) =>
        Assert.Equal(expected, FlightPlanning.TodNm(cruise, target), 6);

    [Fact]
    public void DescentPoint_IsTheTodPlus0To15nm_AndRequestsWithin30nmAreGranted()
    {
        var p = Planning();
        double tod = FlightPlanning.TodNm(35_000, p.ApproachAltitudeFt());
        Assert.InRange(p.DescentPointNm, tod, tod + 15);
        Assert.False(p.DescentDue(p.DescentPointNm + 1));
        Assert.True(p.DescentDue(p.DescentPointNm - 1));
        Assert.True(p.DescentRequestGranted(p.DescentPointNm + 29));
        Assert.False(p.DescentRequestGranted(p.DescentPointNm + 31));
    }

    [Fact]
    public void DescentLevel_IsFL100_OrTheApproachAltitudeForLowCruise()
    {
        Assert.Equal(AltitudeTarget.FlightLevel(100), Planning().DescentLevel);
        Assert.Equal(AltitudeTarget.Feet(3500), Planning(cruise: AltitudeTarget.Feet(9000)).DescentLevel);
    }

    [Fact]
    public void ApproachAltitude_IsFieldPlus3000RoundedUpTo500_OrTheProcedureAltitude()
    {
        Assert.Equal(3500, Planning().ApproachAltitudeFt()); // field 400 ft + 3000 = 3400 -> 3500

        var withProcedure = Route.Airport("BBBB", 400) with
        {
            Runways = [new Runway(new RunwayEnd("09", Route.East(399), 90, "ILS", 4000), new RunwayEnd("27", Route.East(401), 270), 45)],
        };
        var p = Planning(destination: withProcedure);
        p.ChooseArrivalRunway(new Sim.SimState { WindDirectionTrueDeg = 90, WindSpeedKt = 10 });
        Assert.Equal(4000, p.ApproachAltitudeFt());
    }

    [Theory]
    [InlineData(null, 9000, "9000 ft")]
    [InlineData(null, 11000, "FL110")]
    [InlineData(6000, 7000, "FL070")]
    [InlineData(6000, 5000, "5000 ft")]
    public void ToTarget_UsesTheTransitionAltitude(int? ta, int feet, string expected)
    {
        var airport = Route.Airport("AAAA", 0) with { TransitionAltitudeFt = ta };
        Assert.Equal(expected, Planning().ToTarget(feet, airport).ToString());
    }

    [Fact]
    public void Qnh_IsRoundedDown()
    {
        Assert.Equal(1013, FlightPlanning.Qnh(new Sim.SimState { SeaLevelPressureHpa = 1013.9 }));
    }
}
