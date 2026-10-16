using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Replay.Tests.Harness;

namespace OpenSkyAtc.Replay.Tests;

public class IntentScriptTests
{
    [Fact]
    public void Parse_ReadsTimesNamesAndArguments_SortedByTime()
    {
        var script = IntentScript.Parse("""
            # comment
            01:10 RequestPushStart

            00:30 Readback squawk=1234 -initialclimb
            01:02:03.500 Wilco
            """);

        Assert.Equal([TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(70), new TimeSpan(0, 1, 2, 3, 500)], script.Select(s => s.Time));
        Assert.Equal(["Readback", "RequestPushStart", "Wilco"], script.Select(s => s.Name));
        Assert.Equal("1234", script[0].Args["squawk"]);
        Assert.True(script[0].Args.ContainsKey("initialclimb"));
        Assert.Null(script[0].Args["initialclimb"]);
    }

    [Theory]
    [InlineData("00:30")]
    [InlineData("soon RequestClearance")]
    [InlineData("00:30 FlyToTheMoon")]
    [InlineData("00:30 Readback squawk")]
    public void Parse_RejectsBadLines(string line)
    {
        Assert.Throws<FormatException>(() => IntentScript.Parse(line));
    }

    [Fact]
    public void ReadbackOverrides_ReplaceAndRemoveGeneratedItems()
    {
        var generated = new Readback(new Dictionary<ReadbackItem, string>
        {
            [ReadbackItem.Squawk] = "4521",
            [ReadbackItem.InitialClimb] = "5000FT",
        }, ExchangeId: 3);
        var s = IntentScript.Parse("00:01 Readback squawk=1234 -initialclimb")[0];

        var r = Assert.IsType<Readback>(IntentFactory.Create(s, [generated]));

        Assert.Equal(3, r.ExchangeId);
        Assert.Equal("1234", r.Items[ReadbackItem.Squawk]);
        Assert.False(r.Items.ContainsKey(ReadbackItem.InitialClimb));
    }
}
