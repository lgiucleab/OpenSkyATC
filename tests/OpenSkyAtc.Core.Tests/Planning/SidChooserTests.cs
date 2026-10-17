using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;

namespace OpenSkyAtc.Core.Tests.Planning;

/// <summary>Owner's checklist: SimBrief's SID (newer AIRAC) must become one the sim has, for the runway ATC assigns.</summary>
public class SidChooserTests
{
    private static readonly AirportData Lemd = new()
    {
        Icao = "LEMD",
        Runways =
        [
            new Runway(new RunwayEnd("36L", new GeoPoint(40.46, -3.56), 0), new RunwayEnd("18R", new GeoPoint(40.50, -3.56), 180), 60),
            new Runway(new RunwayEnd("14R", new GeoPoint(40.48, -3.58), 140), new RunwayEnd("32L", new GeoPoint(40.46, -3.55), 320), 60),
        ],
        Sids =
        [
            new SidInfo("PINA4N", null, ["36L"]), new SidInfo("PINA4R", null, ["36R"]), new SidInfo("PINA4B", null, ["14R"]),
            new SidInfo("CCS2X", null, ["36L"]), new SidInfo("OLDSID"),
        ],
    };

    private static RunwayEnd End(string d) => Lemd.FindRunway(d)!.First.Designator == d ? Lemd.FindRunway(d)!.First : Lemd.FindRunway(d)!.Second;

    [Theory]
    [InlineData("PINA4N", "36L", "PINA4N")]   // the sim has it for that runway
    [InlineData("PINA3N", "36L", "PINA4N")]   // newer AIRAC: same stem and final letter
    [InlineData("PINA3N", "14R", "PINA4B")]   // ATC changed the runway: the stem's SID for that runway
    [InlineData("PINA4N", "14R", "PINA4B")]
    [InlineData("TOLE1A", "36L", null)]       // no SID with that stem: flight planned route
    [InlineData("OLDSID", "14R", "OLDSID")]   // no runway data in the sim: taken as filed
    [InlineData(null, "36L", null)]
    public void Choose(string? planned, string runway, string? expected) =>
        Assert.Equal(expected, SidChooser.Choose(Lemd, planned, End(runway)));

    [Fact]
    public void WithoutSimSidData_ThePlansSidIsUsed() =>
        Assert.Equal("PINA3N", SidChooser.Choose(Lemd with { Sids = [] }, "PINA3N", End("36L")));

    [Fact]
    public void ThePlansRunway_IsKeptUpTo10ktTailwind_ThenTheWindDecides()
    {
        // Wind from the south: 36L has a tailwind.
        Assert.Equal("36L", RunwaySelector.Select(Lemd, 180, 9, "36L")!.Designator);
        Assert.NotEqual("36L", RunwaySelector.Select(Lemd, 180, 12, "36L")!.Designator);
        // Airport preferred runways (SOPs) still use 5 kt.
        Assert.NotEqual("36L", RunwaySelector.Select(Lemd, 180, 8, null, ["36L"])!.Designator);
    }
}
