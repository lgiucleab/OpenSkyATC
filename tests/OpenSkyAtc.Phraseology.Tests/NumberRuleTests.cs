using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Phraseology.Tests;

/// <summary>The design doc's number-rule table, row by row, plus the edge cases around it.</summary>
public class NumberRuleTests
{
    private readonly Spoken _say = new();

    [Theory]
    [InlineData(120, "FL120", "flight level one two zero")]
    [InlineData(70, "FL70", "flight level seven zero")]
    [InlineData(100, "FL100", "flight level one hundred")]
    [InlineData(300, "FL300", "flight level three hundred")]
    [InlineData(350, "FL350", "flight level three five zero")]
    public void FlightLevel(int level, string display, string speech) =>
        Assert.Equal((display, speech), (Spoken.DisplayAltitude(AltitudeTarget.FlightLevel(level)), _say.Altitude(AltitudeTarget.FlightLevel(level))));

    [Theory]
    [InlineData(5000, "5000 ft", "five thousand feet")]
    [InlineData(3500, "3500 ft", "three thousand five hundred feet")]
    [InlineData(10000, "10000 ft", "one zero thousand feet")]
    [InlineData(12500, "12500 ft", "one two thousand five hundred feet")]
    [InlineData(700, "700 ft", "seven hundred feet")]
    [InlineData(1250, "1250 ft", "one two five zero feet")]
    public void Altitude(int feet, string display, string speech) =>
        Assert.Equal((display, speech), (Spoken.DisplayAltitude(AltitudeTarget.Feet(feet)), _say.Altitude(AltitudeTarget.Feet(feet))));

    [Theory]
    [InlineData(90, "zero nine zero")]
    [InlineData(360, "three six zero")]
    [InlineData(5, "zero zero five")]
    public void Heading(int degrees, string speech) => Assert.Equal(speech, _say.Heading(degrees));

    [Theory]
    [InlineData(121_850, "121.850", "one two one decimal eight five zero")]
    [InlineData(118_100, "118.100", "one one eight decimal one")]
    [InlineData(118_105, "118.105", "one one eight decimal one zero five")]
    [InlineData(124_700, "124.700", "one two four decimal seven")]
    [InlineData(121_000, "121.000", "one two one decimal zero")]
    public void Frequency(int khz, string display, string speech) =>
        Assert.Equal((display, speech), (Spoken.DisplayFrequency(khz), _say.Frequency(khz)));

    [Theory]
    [InlineData(4521, "four five two one")]
    [InlineData(443, "zero four four three")]
    public void Squawk(int code, string speech) => Assert.Equal(speech, _say.Squawk(code));

    [Theory]
    [InlineData(1013, "one zero one three")]
    [InlineData(998, "nine nine eight")]
    public void Qnh(int hpa, string speech) => Assert.Equal(speech, _say.Qnh(hpa));

    [Theory]
    [InlineData("24L", "two four left")]
    [InlineData("06R", "zero six right")]
    [InlineData("18C", "one eight centre")]
    [InlineData("36", "three six")]
    public void Runway(string designator, string speech) => Assert.Equal(speech, _say.Runway(designator));

    [Theory]
    [InlineData(250, 12, "two five zero degrees, one two knots")]
    [InlineData(50, 7, "zero five zero degrees, seven knots")]
    public void Wind(int dir, int kt, string speech) => Assert.Equal(speech, _say.Wind(dir, kt));

    [Fact]
    public void NinerAndTree_OnlyWhenEnabled()
    {
        Assert.Equal("three nine", new Spoken(ninerTree: false).Digits("39"));
        Assert.Equal("tree niner", new Spoken(ninerTree: true).Digits("39"));
        Assert.Equal("flight level tree niner zero", new Spoken(ninerTree: true).Altitude(AltitudeTarget.FlightLevel(390)));
    }

    [Theory]
    [InlineData("LEMD", "Lima Echo Mike Delta")]
    [InlineData("A1", "Alpha one")]
    [InlineData("EC-ABC", "Echo Charlie Alpha Bravo Charlie")]
    public void Spell(string text, string speech) => Assert.Equal(speech, _say.Spell(text));

    [Theory]
    [InlineData("NAPSI1C", "Napsi one Charlie")]
    [InlineData("TEST1A", "Test one Alpha")]
    [InlineData("NAPSI", "Napsi")]
    [InlineData("BCN", "Bravo Charlie November")]
    [InlineData("K", "Kilo")]
    [InlineData("M2", "Mike two")]
    public void Designator(string text, string speech) => Assert.Equal(speech, _say.Designator(text));
}
