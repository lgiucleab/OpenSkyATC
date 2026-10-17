using OpenSkyAtc.Core.Brain;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Tests.Planning;
using OpenSkyAtc.TaxiArrows;
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

    [Fact]
    public void Arrows_FollowTheReadbacks_AndStopAtTheHoldShortUntilTheCrossingIsReadBack()
    {
        var controller = new TaxiArrowController();
        var pool = new ArrowPool();
        var samples = new List<(TimeSpan Time, int Count, double FarthestNorthM, bool CrossReadBack)>();
        var holdShort = SyntheticAirports.At(1500, 60); // the hold-short line of 09/27
        int maxArrows = 0;

        ReplayRunner.Run(new AtcBrain(), Build(), IntentScript.Parse("""
            00:10 RequestClearance
            00:45 RequestPushStart
            01:40 RequestTaxi
            """), Plan, Origin, Destination, seed: 1, new ReplayOptions { Phraseology = Fixed }, afterTick: (brain, s) =>
        {
            var arrows = controller.Update(brain.Session, s);
            pool.Update(arrows);
            maxArrows = Math.Max(maxArrows, pool.Count);
            // How far south of the hold-short the arrows reach (positive = beyond it, across the runway).
            double beyond = arrows.Count == 0 ? double.NaN : arrows.Max(a => holdShort.Latitude - a.Position.Latitude) * 111_000;
            samples.Add((s.Time, arrows.Count, beyond, brain.Session.Clearance.IsCrossReadBack("27")));
        });

        // Taxi clearance at 01:42.75, read back at 01:46.75 (auto-readback after 4 s).
        Assert.All(samples.Where(x => x.Time < TimeSpan.FromSeconds(106.5)), x => Assert.Equal(0, x.Count));
        Assert.Contains(samples, x => x.Time < TimeSpan.FromSeconds(108) && x.Count > 0);
        // Before the crossing is read back, no arrow is past the hold-short line.
        Assert.All(samples.Where(x => x.Count > 0 && !x.CrossReadBack), x => Assert.True(x.FarthestNorthM < 0.5, $"{x.Time}: {x.FarthestNorthM:F1} m beyond"));
        Assert.Contains(samples, x => x.CrossReadBack && x.FarthestNorthM > 50);
        // Removed at the holding point and nothing left at the end.
        Assert.Equal(0, samples[^1].Count);
        Assert.Equal(0, pool.Count);
        Assert.InRange(maxArrows, 20, 31);
    }
}
