using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;
using static OpenSkyAtc.Core.Tests.Planning.SyntheticAirports;

namespace OpenSkyAtc.Core.Tests.Planning;

/// <summary>Owner's request: the push ends with the nose facing the way the taxi route starts.</summary>
public class PushbackPlannerTests
{
    private static readonly AirportData Apron = FacilityConverter.ToAirportData(PushbackApron());
    private static readonly GraphTaxiPlanner Planner = new();

    [Theory]
    [InlineData("09", 270, "west")] // runway 09 starts at the west end: the route goes west on T, then W
    [InlineData("27", 90, "east")]  // runway 27 starts at the east end: east on T, then E
    public void PushesStraightBackToTheTaxilane_ThenFacesTheRouteDirection(string runway, double heading, string facing)
    {
        var r = Apron.FindRunway(runway)!;
        var plan = PushbackPlanner.Plan(Apron, At(0, 60), 0, r.First.Designator == runway ? r.First : r.Second, Planner)!;

        Assert.Equal(60, plan.StraightBackM, 0);       // back to taxilane T
        Assert.Equal(heading, plan.FinalHeadingTrueDeg, 0);
        Assert.Equal(facing, plan.Facing);
    }

    [Fact]
    public void NotAtAStand_NoPushback() =>
        Assert.Null(PushbackPlanner.Plan(Apron, At(0, -100), 0, Apron.Runways[0].First, Planner));

    [Fact]
    public void NeverASwingRound_OnlySideways()
    {
        var plan = PushbackPlanner.Plan(Apron, At(0, 60), 0, null, Planner)!;
        Assert.InRange(Math.Abs(GeoMath.AngleDiffDeg(plan.FinalHeadingTrueDeg, 0)), 45, 135);
    }

    [Theory]
    [InlineData(0, "north")]
    [InlineData(44, "northeast")]
    [InlineData(181, "south")]
    [InlineData(292, "west")]
    [InlineData(338, "north")]
    [InlineData(-90, "west")]
    public void CompassWords(double heading, string word) => Assert.Equal(word, PushbackPlanner.Compass(heading));
}
