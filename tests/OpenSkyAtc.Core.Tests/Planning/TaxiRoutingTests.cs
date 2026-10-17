using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;
using OpenSkyAtc.Core.Sim;
using static OpenSkyAtc.Core.Tests.Planning.SyntheticAirports;

namespace OpenSkyAtc.Core.Tests.Planning;

public class FacilityConverterTests
{
    private static readonly AirportData Grid = FacilityConverter.ToAirportData(SyntheticAirports.Grid());

    [Fact]
    public void Runways_HaveBothEndsWithTrueHeadings()
    {
        var r09 = Grid.FindRunway("09")!;
        Assert.Equal("09/27", r09.Name);
        Assert.Equal(90, r09.First.HeadingTrueDeg, 3);
        Assert.Equal(270, r09.Second.HeadingTrueDeg, 3);
        Assert.True(GeoMath.DistanceM(r09.First.Position, At(0, 0)) < 1);
        Assert.True(GeoMath.DistanceM(r09.Second.Position, At(3000, 0)) < 1);
        Assert.Equal(45, r09.WidthM);
    }

    [Fact]
    public void Frequencies_BecomeUnitsWithPlaceNames_AndCenterIsSeparate()
    {
        Assert.Contains(Grid.Frequencies, f => f.Role == ControllerRole.Delivery && f.Callsign == "Gridtown Delivery" && f.FrequencyKhz == 121_850);
        Assert.Contains(Grid.Frequencies, f => f.Role == ControllerRole.Departure && f.Callsign == "Grid City Departure");
        Assert.DoesNotContain(Grid.Frequencies, f => f.Role == ControllerRole.Center);
        var centre = Assert.Single(FacilityConverter.CenterFrequencies(SyntheticAirports.Grid()));
        Assert.Equal(132_350, centre.FrequencyKhz);
        Assert.Equal("Grid City", Grid.Name); // spoken name from the radar frequency
    }

    [Theory]
    [InlineData("PALMA CONTROL", "Palma")]
    [InlineData("DE GAULLE", "De Gaulle")]
    [InlineData("LFPG", null)]
    [InlineData("SOCAL APPROACH", "Socal")]
    [InlineData("TOWER", null)]
    [InlineData("", null)]
    public void StationNames_DropRoleWordsAndBareIcao(string raw, string? expected)
    {
        var name = FacilityConverter.StationName(raw, "LFPG");
        Assert.Equal(expected, name is null ? null : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant()));
    }

    [Fact]
    public void UnnamedOrIcaoNamedFrequencies_TakeTheTowerName()
    {
        var dump = SyntheticAirports.Grid() with
        {
            Frequencies = [new FacilityFrequency(1, 127_130_000, "XGRD"), new FacilityFrequency(6, 118_100_000, "GRIDTOWN TOWER"), new FacilityFrequency(8, 120_000_000, "GRID CITY CONTROL")],
        };
        var a = FacilityConverter.ToAirportData(dump);
        Assert.Contains(a.Frequencies, f => f.Callsign == "Gridtown Information");
        Assert.Contains(a.Frequencies, f => f.Callsign == "Gridtown Tower");
        Assert.Contains(a.Frequencies, f => f.Callsign == "Grid City Approach");
        Assert.Equal("Grid City", a.Name);
    }

    [Fact]
    public void AirportFields_Convert()
    {
        Assert.Equal(6000, Grid.TransitionAltitudeFt);
        Assert.Equal(328, Grid.ElevationFt);
        Assert.Equal(["GRID1A"], Grid.Sids.Select(s => s.Name));
        Assert.Equal(["GRID1B"], Grid.Stars);
        var r18 = Grid.FindRunway("18")!.First;
        Assert.Equal("ILS", r18.ApproachType);
        Assert.Equal(3000, r18.ApproachAltitudeFt);
    }

    [Fact]
    public void HoldShorts_AreAssignedToTheNearestRunway()
    {
        var graph = Grid.Taxi!;
        var holds = graph.Nodes.Where(n => n.Kind == TaxiNodeKind.HoldShort).ToList();
        Assert.Equal(3, holds.Count);
        Assert.Equal(2, holds.Count(h => h.RunwayDesignator == "09"));
        Assert.Single(holds, h => h.RunwayDesignator == "18");
        Assert.Equal(3, Grid.HoldShorts.Count);
        Assert.Contains(Grid.HoldingPoints, h => h.RunwayDesignator == "36"); // serves both ends
    }

    [Theory]
    [InlineData(31, 527, 0, "T527")]
    [InlineData(10, 12, 0, "12")]
    [InlineData(1, 5, 0, "5")]
    [InlineData(2, 3, 0, "N3")]
    [InlineData(12, 4, 2, "A4B")]
    public void StandNames(int name, uint number, int suffix, string expected) =>
        Assert.Equal(expected, FacilityConverter.StandName(new FacilityParking(0, 9, name, suffix, number, 0, 20, 0, 0)));

    [Fact]
    public void ServiceRoads_AreNotInTheGraph_ButApronLanesAre()
    {
        var kinds = Grid.Taxi!.Edges.Select(e => e.Kind).ToList();
        Assert.Contains(TaxiEdgeKind.Apron, kinds);
        Assert.Equal(SyntheticAirports.Grid().TaxiPaths.Count - 1, kinds.Count); // all but the road
    }

    [Fact]
    public void FacilityDump_RoundTripsAsJson()
    {
        var dump = SyntheticAirports.Grid();
        var back = FacilityDump.Parse(dump.ToJson());
        Assert.Equal(dump.TaxiPaths, back.TaxiPaths);
        Assert.Equal(dump.Runways, back.Runways);
        Assert.Throws<FormatException>(() => FacilityDump.Parse("{\"format\":\"other\",\"icao\":\"X\"}"));
    }
}

public class TaxiRoutingTests
{
    private static readonly AirportData Grid = FacilityConverter.ToAirportData(SyntheticAirports.Grid());
    private static readonly GraphTaxiPlanner Planner = new();
    private static RunwayEnd Rwy18 => Grid.FindRunway("18")!.First;

    [Fact]
    public void FromAStand_CrossesTheRunway_ViaABC_WithAHoldShort()
    {
        var plan = Planner.PlanToHoldingPoint(Grid, At(1000, 400), Rwy18);

        Assert.False(plan.IsFallback, plan.FallbackReason);
        Assert.Equal(["A", "B", "C"], plan.Via);          // the unnamed gap in B is bridged, the lane out of the stand ignored
        Assert.True(Grid.FindRunway("09")!.Has(Assert.Single(plan.HoldShorts)), "hold short of 09/27 (named by its nearer end)");
        var crossing = Assert.Single(plan.Crossings);
        Assert.True(GeoMath.DistanceM(crossing.HoldShort, At(1500, 60)) < 1, "hold short before entering the runway");
        Assert.True(GeoMath.DistanceM(plan.Target, At(1500, -260)) < 1, "ends at the runway 18 holding point");
        Assert.Equal("18", plan.HoldingPoint!.RunwayDesignator);
        Assert.True(GeoMath.DistanceM(plan.Path[0], At(1000, 400)) < 1);
    }

    [Fact]
    public void ACostlyCrossing_SendsTheRouteAroundTheRunwayEnd()
    {
        var planner = new GraphTaxiPlanner(new TaxiRoutingOptions { RunwayCrossingPenaltyM = 20_000 });
        var plan = planner.PlanToHoldingPoint(Grid, At(1000, 400), Rwy18);
        Assert.Equal(["A", "D", "E", "C"], plan.Via);
        Assert.Empty(plan.HoldShorts);
    }

    [Fact]
    public void FromTheNetwork_StartsAtTheNearestTaxiwayNode_NotARunwayNode()
    {
        // The runway 27 end node (3000, 0) is nearer than A's east end (3100, 150), but only runway paths touch it.
        var plan = Planner.PlanToHoldingPoint(Grid, At(2900, 160), Rwy18);
        Assert.False(plan.IsFallback, plan.FallbackReason);
        Assert.Equal(["D", "E", "C"], plan.Via);
    }

    [Fact]
    public void UnnamedNetwork_FallsBack_ButKeepsTheHoldingPoint()
    {
        var bad = FacilityConverter.ToAirportData(SyntheticAirports.Grid(unnamed: true, icao: "XBAD"));
        var plan = Planner.PlanToHoldingPoint(bad, At(1000, 400), bad.FindRunway("18")!.First);
        Assert.True(plan.IsFallback);
        Assert.Contains("named", plan.FallbackReason);
        Assert.True(GeoMath.DistanceM(plan.Target, At(1500, -260)) < 1);
        Assert.Empty(plan.Path); // no route: the off-route check is off for this taxi
    }

    [Fact]
    public void NoTaxiNetwork_FallsBack()
    {
        var none = FacilityConverter.ToAirportData(SyntheticAirports.NoTaxiNetwork());
        var plan = Planner.PlanToHoldingPoint(none, At(1000, 400), none.FindRunway("18")!.First);
        Assert.True(plan.IsFallback);
        Assert.NotNull(plan.FallbackReason);
    }

    [Fact]
    public void FarFromTheNetwork_FallsBack()
    {
        var plan = Planner.PlanToHoldingPoint(Grid, At(9000, 9000), Rwy18);
        Assert.True(plan.IsFallback);
        Assert.Contains("not near", plan.FallbackReason);
    }

    [Fact]
    public void NeverCrossesTheDepartureRunway_ToReachAHoldingPointOnTheFarSide()
    {
        // Owner's LEMD flight: taxiing up Y on the east side of 36R, the route crossed just behind the threshold to
        // a west-side hold-short (nearer the threshold) and led the aircraft onto the runway.
        var airport = FacilityConverter.ToAirportData(SyntheticAirports.ThresholdShortcut());
        var plan = Planner.PlanToHoldingPoint(airport, At(300, 600), airport.FindRunway("36")!.First);

        Assert.False(plan.IsFallback, plan.FallbackReason);
        Assert.Equal(["A", "Y", "E1"], plan.Via);
        Assert.True(GeoMath.DistanceM(plan.Target, At(80, 100)) < 1, "ends at the near-side hold-short");
        Assert.All(plan.Path, p => Assert.True(Math.Abs(airport.FindRunway("36")!.Project(p).Cross) > 22.5, "no point of the route on the runway"));
    }

    [Fact]
    public void TaxiIn_ToThePlannedStand_OrTheNearestGate()
    {
        var exit = At(1500, -200);
        Assert.Equal("12", Planner.ChooseStand(Grid, "Gate 12", exit, EngineType.Jet)!.Name);
        Assert.Equal("11", Planner.ChooseStand(Grid, null, At(900, 200), EngineType.Jet)!.Name);

        var plan = Planner.PlanToStand(Grid, exit, Planner.ChooseStand(Grid, "12", exit, EngineType.Jet));
        Assert.False(plan.IsFallback, plan.FallbackReason);
        Assert.Equal(["B", "A"], plan.Via);
        Assert.True(Grid.FindRunway("09")!.Has(Assert.Single(plan.HoldShorts)));
        Assert.True(GeoMath.DistanceM(plan.Target, At(2000, 400)) < 1);
    }
}
