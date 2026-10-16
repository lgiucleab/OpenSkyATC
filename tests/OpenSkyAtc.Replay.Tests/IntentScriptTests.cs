using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Replay.Tests.Harness;

namespace OpenSkyAtc.Replay.Tests;

public class IntentScriptTests
{
    [Fact]
    public void Parse_ReadsTimesAndIntents_SortedByTime()
    {
        var script = IntentScript.Parse("""
            # comment
            01:10 RequestPushStart

            00:30 RequestClearance
            01:02:03.500 Wilco
            """);

        Assert.Equal(
            [
                new ScriptedIntent(TimeSpan.FromSeconds(30), new PilotIntent(PilotIntentKind.RequestClearance)),
                new ScriptedIntent(TimeSpan.FromSeconds(70), new PilotIntent(PilotIntentKind.RequestPushStart)),
                new ScriptedIntent(new TimeSpan(0, 1, 2, 3, 500), new PilotIntent(PilotIntentKind.Wilco)),
            ],
            script);
    }

    [Theory]
    [InlineData("00:30")]
    [InlineData("soon RequestClearance")]
    [InlineData("00:30 FlyToTheMoon")]
    public void Parse_RejectsBadLines(string line)
    {
        Assert.Throws<FormatException>(() => IntentScript.Parse(line));
    }
}
