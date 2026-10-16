using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Phraseology;
using OpenSkyAtc.Replay.Tests.Harness;
using static OpenSkyAtc.Replay.Tests.Harness.SyntheticGridTaxi;

namespace OpenSkyAtc.Replay.Tests;

/// <summary>Taxi-out on a real-format taxi network: a routed clearance, a runway crossing, and the handoff.</summary>
public class GraphTaxiTests
{
    private static readonly PhraseologyOptions Fixed = new() { EuropeanVariants = false, Courtesy = false, StationNameOnFirstContact = false };

    [Fact]
    public void TaxiOut_IsRouted_AndTheRunwayCrossingIsClearedAtTheHoldShort()
    {
        var t = ReplayRunner.Run(new AtcBrain(), Build(), IntentScript.Parse("""
            00:10 RequestClearance
            00:45 RequestPushStart
            01:40 RequestTaxi
            """), Plan, Origin, Destination, seed: 1, new ReplayOptions { Phraseology = Fixed });
        Fixtures.VerifyTranscript("taxi-graph-crossing", t);

        Assert.Contains(t, l => l.Contains("runway 18 via A, B, C, hold short runway 27"));
        Assert.Single(t, l => l.Contains("cross runway 27"));
        Assert.Contains(t, l => l.Contains("cross: correct"));
        Assert.DoesNotContain(t, l => l.Contains("CONFORMANCE"));
        int cross = t.ToList().FindIndex(l => l.Contains("cross runway 27"));
        int handoff = t.ToList().FindIndex(l => l.Contains("contact Gridtown Tower"));
        Assert.InRange(cross, 0, handoff - 1);
    }
}
