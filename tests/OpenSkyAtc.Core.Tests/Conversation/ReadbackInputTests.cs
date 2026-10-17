using OpenSkyAtc.Core.Conversation;

namespace OpenSkyAtc.Core.Tests.Conversation;

public class ReadbackInputTests
{
    [Theory]
    [InlineData(ReadbackItem.Frequency, "121.63", "121630")]
    [InlineData(ReadbackItem.Frequency, "121,630", "121630")]
    [InlineData(ReadbackItem.Frequency, "118.1", "118100")]
    [InlineData(ReadbackItem.Frequency, "121630", "121630")]
    [InlineData(ReadbackItem.Frequency, "99.5", "99.5")]
    [InlineData(ReadbackItem.InitialClimb, "5000", "5000FT")]
    [InlineData(ReadbackItem.InitialClimb, "5,000 feet", "5000FT")]
    [InlineData(ReadbackItem.Altitude, "FL 120", "FL120")]
    [InlineData(ReadbackItem.Altitude, "flight level 90", "FL090")]
    [InlineData(ReadbackItem.Squawk, " 4774 ", "4774")]
    [InlineData(ReadbackItem.Squawk, "477", "477")]
    [InlineData(ReadbackItem.Heading, "90", "090")]
    [InlineData(ReadbackItem.Heading, "heading 0", "360")]
    [InlineData(ReadbackItem.Runway, "rwy 36l", "36L")]
    [InlineData(ReadbackItem.Runway, "runway 3 6 left", "36L")]
    [InlineData(ReadbackItem.Runway, "9", "09")]
    [InlineData(ReadbackItem.HoldShorts, "06 and 24L", "06,24L")]
    [InlineData(ReadbackItem.Route, "flight plan", "FLIGHT PLANNED ROUTE")]
    [InlineData(ReadbackItem.Route, "cind 5f", "CIND5F")]
    [InlineData(ReadbackItem.HoldingPoint, "holding point A 1", "A1")]
    [InlineData(ReadbackItem.Stand, "gate T527", "T527")]
    [InlineData(ReadbackItem.Clearance, "line up and wait", "LINEUP")]
    [InlineData(ReadbackItem.Clearance, "cleared for take-off", "TAKEOFF")]
    [InlineData(ReadbackItem.Speed, "180 kt", "180")]
    [InlineData(ReadbackItem.Destination, " lebl ", "LEBL")]
    [InlineData(ReadbackItem.Squawk, "", "")]
    public void Normalize(ReadbackItem item, string typed, string expected) =>
        Assert.Equal(expected, ReadbackInput.Normalize(item, typed));

    [Fact]
    public void TypedValues_MatchTheBrainsExpectedValues()
    {
        var expected = new List<KeyValuePair<ReadbackItem, string>>
        {
            new(ReadbackItem.Frequency, ReadbackValues.Frequency(121_630)),
            new(ReadbackItem.InitialClimb, ReadbackValues.Altitude(Model.AltitudeTarget.Feet(5000))),
            new(ReadbackItem.HoldShorts, ReadbackValues.HoldShorts(["24L", "06"])),
        };
        var typed = new Dictionary<ReadbackItem, string>
        {
            [ReadbackItem.Frequency] = ReadbackInput.Normalize(ReadbackItem.Frequency, "121.63"),
            [ReadbackItem.InitialClimb] = ReadbackInput.Normalize(ReadbackItem.InitialClimb, "5000 ft"),
            [ReadbackItem.HoldShorts] = ReadbackInput.Normalize(ReadbackItem.HoldShorts, "06, 24L"),
        };
        Assert.True(ReadbackVerifier.Check(expected, typed).IsCorrect);
    }
}
