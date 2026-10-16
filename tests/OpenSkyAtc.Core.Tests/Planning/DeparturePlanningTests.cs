using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Planning;
using OpenSkyAtc.Core.Random;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Core.Tests.Planning;

public class RunwaySelectorTests
{
    private static readonly AirportData Airport = new()
    {
        Icao = "TEST",
        Runways =
        [
            new Runway(new RunwayEnd("06", new GeoPoint(0, 0), 60), new RunwayEnd("24", new GeoPoint(0.01, 0.02), 240), 45),
            new Runway(new RunwayEnd("15", new GeoPoint(0, 0), 150), new RunwayEnd("33", new GeoPoint(-0.02, 0.01), 330), 45),
        ],
    };

    [Theory]
    [InlineData(240, 15, "24")]
    [InlineData(60, 15, "06")]
    [InlineData(320, 10, "33")]
    [InlineData(160, 10, "15")]
    public void PicksTheHighestHeadwind(double windFrom, double kt, string expected)
    {
        Assert.Equal(expected, RunwaySelector.Select(Airport, windFrom, kt, hint: null)!.Designator);
    }

    [Fact]
    public void UsesThePlanHint_WhenTheRunwayExists()
    {
        Assert.Equal("06", RunwaySelector.Select(Airport, 240, 15, hint: "06")!.Designator);
        Assert.Equal("24", RunwaySelector.Select(Airport, 240, 15, hint: "99")!.Designator);
    }

    [Fact]
    public void Headwind_FollowsTheDocFormula()
    {
        var end = Airport.Runways[0].Second; // 240
        Assert.Equal(15, RunwaySelector.HeadwindKt(end, 240, 15), 6);
        Assert.Equal(0, RunwaySelector.HeadwindKt(end, 330, 15), 6);
        Assert.Equal(-15, RunwaySelector.HeadwindKt(end, 60, 15), 6);
    }

    [Fact]
    public void NoRunways_ReturnsNull()
    {
        Assert.Null(RunwaySelector.Select(new AirportData { Icao = "NONE" }, 0, 0, null));
    }
}

public class SquawkAllocatorTests
{
    [Fact]
    public void Fallback_NeverPicksReservedOrInvalidCodes()
    {
        var rng = new SeededRandom(3);
        for (int i = 0; i < 3000; i++)
        {
            int code = SquawkAllocator.Allocate(SquawkConfig.Empty, "LEBL", rng);
            Assert.True(SquawkAllocator.IsValidCode(code), $"{code} is not a valid octal code");
            Assert.DoesNotContain(code, SquawkAllocator.Reserved);
        }
    }

    [Fact]
    public void ConfiguredRange_UsesTheLongestMatchingPrefix()
    {
        var config = new SquawkConfig([new SquawkRange("LE", 4501, 4577), new SquawkRange("LEBL", 3001, 3007)]);
        var rng = new SeededRandom(5);
        for (int i = 0; i < 200; i++)
        {
            Assert.InRange(SquawkAllocator.Allocate(config, "LEBL", rng), 3001, 3007);
            int madrid = SquawkAllocator.Allocate(config, "LEMD", rng);
            Assert.InRange(madrid, 4501, 4577);
            Assert.True(SquawkAllocator.IsValidCode(madrid));
        }
    }

    [Fact]
    public void RangeOfOnlyReservedCodes_Throws()
    {
        var config = new SquawkConfig([new SquawkRange("X", 7700, 7700)]);
        Assert.Throws<InvalidOperationException>(() => SquawkAllocator.Allocate(config, "XTST", new SeededRandom(1)));
    }

    [Theory]
    [InlineData(4521, true)]
    [InlineData(7777, true)]
    [InlineData(1280, false)]
    [InlineData(8000, false)]
    public void IsValidCode_ChecksOctalDigits(int code, bool valid) => Assert.Equal(valid, SquawkAllocator.IsValidCode(code));
}

public class InitialClimbAndWindTests
{
    [Theory]
    [InlineData(null, "5000 ft")]
    [InlineData(6000, "5000 ft")]
    [InlineData(5000, "FL070")]
    [InlineData(3000, "FL070")]
    public void InitialClimb_DependsOnTransitionAltitude(int? ta, string expected)
    {
        Assert.Equal(expected, InitialClimbRule.For(new AirportData { Icao = "X", TransitionAltitudeFt = ta }).ToString());
    }

    [Theory]
    [InlineData(250, 12, 0, 0, 250, 12)]     // no variation
    [InlineData(254, 12, 4, 0, 250, 12)]     // 4 deg east variation: 254 true = 250 magnetic
    [InlineData(5, 10, 0, 0, 360, 10)]       // rounds to north, reported as 360
    [InlineData(250, 0.4, 0, 0, 0, 0)]       // calm
    public void Wind_IsMagneticAndRounded(double fromTrue, double kt, double headingTrue, double headingMag, int dir, int speed)
    {
        // Variation = true - magnetic heading; express it through the aircraft heading.
        var s = new SimState { WindDirectionTrueDeg = fromTrue, WindSpeedKt = kt, HeadingTrueDeg = 90 + headingTrue, HeadingMagneticDeg = 90 + headingMag };
        var wind = WindReport.From(s);
        Assert.Equal(dir, wind.DirectionMagDeg);
        Assert.Equal(speed, wind.SpeedKt);
    }
}
