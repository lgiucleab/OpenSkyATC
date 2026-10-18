using OpenSkyAtc.Core.Conformance;
using OpenSkyAtc.Core.Conversation;
using OpenSkyAtc.Core.Debrief;
using OpenSkyAtc.Replay.Tests.Harness;

namespace OpenSkyAtc.Replay.Tests;

/// <summary>Step 16 done-when: a replayed flight with known errors produces the expected debrief.</summary>
public class DebriefTests
{
    private static FlightDebrief Fly(SimulatedPilotOptions options)
    {
        var pilot = new SimulatedPilot(options);
        pilot.Fly(seed: 3);
        Assert.True(pilot.Brain.Session.Ended, "the flight did not end");
        return DebriefBuilder.Build(pilot.Brain.Session, SyntheticRoute.Plan);
    }

    [Fact]
    public void ACleanFlight_Scores100_WithNoFindings()
    {
        var debrief = Fly(new SimulatedPilotOptions());
        Assert.Equal(100, debrief.Score);
        Assert.Empty(debrief.Findings);
        Assert.True(debrief.Completed);
        Assert.Equal(debrief.Readbacks, debrief.ReadbacksCorrect);
        Assert.All(debrief.Handoffs, h => Assert.NotNull(h.Seconds));
    }

    [Fact]
    public void AFlightWithKnownErrors_GetsTheExpectedDebrief()
    {
        var debrief = Fly(new SimulatedPilotOptions
        {
            WrongSquawkReadback = true,
            LateFrequencyReadback = TimeSpan.FromSeconds(30),
            LateAirborneHandoff = TimeSpan.FromSeconds(70),
            AltitudeOvershootFt = 700,
        });

        var wrong = Assert.Single(debrief.Findings, f => f.Kind == FindingKind.ReadbackWrong);
        Assert.Equal("ifr-clearance", wrong.Tag);
        Assert.Equal([ReadbackItem.Squawk], wrong.Wrong);
        Assert.Single(debrief.Findings, f => f.Kind == FindingKind.ReadbackMissed);
        var late = Assert.Single(debrief.Findings, f => f.Kind == FindingKind.HandoffLate);
        Assert.InRange(late.Seconds!.Value, 70, 80);
        var bust = Assert.Single(debrief.Findings, f => f.Check == ConformanceCheck.AltitudeBust);
        Assert.InRange(bust.Worst!.Value, 300, 750);
        Assert.Equal(debrief.Readbacks - 2, debrief.ReadbacksCorrect);
        Assert.Equal(100 - debrief.Findings.Sum(f => f.Penalty), debrief.Score);
        Assert.True(debrief.Score < 100);

        Fixtures.VerifyTranscript("debrief-known-errors", DebriefFormat.ToJson(debrief).ReplaceLineEndings("\n").Split('\n'));
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var debrief = Fly(new SimulatedPilotOptions { WrongSquawkReadback = true });
        string json = DebriefFormat.ToJson(debrief);
        var back = DebriefFormat.FromJson(json);
        Assert.NotNull(back);
        Assert.Equal(json, DebriefFormat.ToJson(back));
        Assert.Null(DebriefFormat.FromJson("{\"version\":2}"));
        Assert.Null(DebriefFormat.FromJson("not json"));
    }
}
